using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace NutBoltSort
{
    [Serializable]
    public class DifficultyTier
    {
        public string name = "Beginner";
        [Min(1)] public int minLevel = 1;
        [Min(1)] public int maxLevel = 5;
        [Min(2)] public int activeColorCount = 3;
        [Min(1)] public int emptyBoltCount = 2;
        [Min(1)] public int targetInverseSteps = 24;
        [Min(1)] public int minimumAcceptedSteps = 12;
        [Min(0)] public int minimumMixedBolts = 2;
        [Min(0)] public int minimumColorTransitions = 3;
        [Min(0)] public int maximumCompletedBoltsAtStart = 1;
        [Min(1)] public int minimumGuaranteedSolutionLength = 12;
        [Min(1)] public int minimumStartingLegalMoves = 1;
        [Min(0)] public int maximumStartingLegalMoves = 0; // zero means unlimited
        [Min(1)] public int maxGenerationAttempts = 24;
    }

    public enum GenerationFailure
    {
        None, InvalidConfiguration, NoValidInverseMove, InsufficientAcceptedSteps,
        InvalidColorCount, IncorrectEmptyBoltCount, SolutionReplayFailed, PuzzleAlreadySolved,
        PuzzleOneMoveFromSolved, InsufficientMixedBolts, InsufficientTransitions,
        TooManyCompletedBolts, NoStartingLegalMoves, DuplicateSignature
    }

    [Serializable]
    public struct LogicalMove
    {
        public int sourceBoltIndex;
        public int destinationBoltIndex;
        public NutColor color;
        public int count;
    }

    /// <summary>
    /// Data-only endless puzzle generator.  It deliberately has no knowledge of scene objects,
    /// transforms, animation or layout.  The existing LevelManager receives its LevelDataSO unchanged.
    /// </summary>
    public sealed class ProceduralLevelGenerator : MonoBehaviour
    {
        [Header("Generator")]
        [SerializeField, Min(1)] private int generatorVersion = 1;
        [SerializeField, Min(1)] private int startingLevelIndex = 1;
        [SerializeField] private List<NutColor> supportedColors = new List<NutColor>
            { NutColor.Red, NutColor.Blue, NutColor.Green, NutColor.Yellow };
        [SerializeField] private List<DifficultyTier> difficultyTiers = new List<DifficultyTier>
        {
            new DifficultyTier { name = "Levels 1-5", minLevel = 1, maxLevel = 5, activeColorCount = 3, targetInverseSteps = 24, minimumAcceptedSteps = 12, minimumMixedBolts = 2, minimumColorTransitions = 3, minimumGuaranteedSolutionLength = 12 },
            new DifficultyTier { name = "Levels 6+", minLevel = 6, maxLevel = 999999, activeColorCount = 4, targetInverseSteps = 64, minimumAcceptedSteps = 28, minimumMixedBolts = 3, minimumColorTransitions = 6, maximumCompletedBoltsAtStart = 0, minimumGuaranteedSolutionLength = 28 }
        };
        [SerializeField, Min(1)] private int maximumGenerationAttempts = 32;
        [SerializeField, Min(1)] private int maximumInverseMoveAttemptsPerStep = 24;
        [SerializeField, Range(1, 50)] private int recentSignatureHistorySize = 20;
        [SerializeField] private bool enableDebugLogging = true;
        [SerializeField] private bool storeGuaranteedSolutionPath = true;
        [SerializeField] private bool useDeterministicTestSeed;
        [SerializeField] private int testSeed = 12345;

        private readonly Queue<string> recentSignatures = new Queue<string>();
        private LevelDataSO currentSnapshot;
        private List<LogicalMove> currentSolution = new List<LogicalMove>();
        private int currentLevelIndex = -1;
        private int currentSeed;

        public int CurrentSeed => currentSeed;
        public string CurrentSignature => currentSnapshot != null ? currentSnapshot.puzzleSignature : string.Empty;
        public IReadOnlyList<LogicalMove> CurrentGuaranteedSolution => currentSolution;

        public LevelDataSO GetOrGenerateCurrentLevel(int requestedIndex, int capacity)
        {
            int level = Mathf.Max(startingLevelIndex, requestedIndex + startingLevelIndex);
            if (currentSnapshot != null && currentLevelIndex == level)
                return currentSnapshot.DeepCopy(); // Restart source of truth.

            return GenerateAndSave(level, capacity, useDeterministicTestSeed ? testSeed : NewSeed());
        }

        public void AdvanceToNextLevel(int requestedIndex)
        {
            currentSnapshot = null;
            currentSolution.Clear();
            currentLevelIndex = -1;
        }

        [ContextMenu("Generate New Level")]
        public void GenerateNewLevel()
        {
            int capacity = BoltView.Capacity;
            int level = currentLevelIndex > 0 ? currentLevelIndex + 1 : startingLevelIndex;
            GenerateAndSave(level, capacity, useDeterministicTestSeed ? testSeed : NewSeed());
        }

        [ContextMenu("Reload Saved Current Level")]
        public void ReloadSavedCurrentLevel()
        {
            var manager = FindObjectOfType<LevelManager>();
            if (manager != null && currentSnapshot != null) manager.BuildLevel(currentSnapshot.DeepCopy(), out _);
        }

        [ContextMenu("Generate Using Seed")]
        public void GenerateUsingSeed() => GenerateAndSave(currentLevelIndex > 0 ? currentLevelIndex : startingLevelIndex, BoltView.Capacity, testSeed);
        [ContextMenu("Print Current Seed")]
        public void PrintCurrentSeed() => Debug.Log("[ProceduralLevelGenerator] Seed: " + currentSeed);
        [ContextMenu("Print Puzzle Signature")]
        public void PrintPuzzleSignature() => Debug.Log("[ProceduralLevelGenerator] Signature: " + CurrentSignature);
        [ContextMenu("Validate Current Puzzle")]
        public void ValidateCurrentPuzzle() => Debug.Log("[ProceduralLevelGenerator] " + ValidateSavedCurrent(BoltView.Capacity));
        [ContextMenu("Replay Guaranteed Solution Logically")]
        public void ReplayGuaranteedSolutionLogically() => Debug.Log("[ProceduralLevelGenerator] Replay " + (ReplaySaved(BoltView.Capacity) ? "passed" : "failed"));

        // Enter the desired count in the component's context menu only for quick smoke tests.
        [ContextMenu("Batch Generate 100 Levels")]
        public void BatchGenerate100Levels() => BatchGenerate(100, BoltView.Capacity);

        public void BatchGenerate(int count, int capacity)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew(); int ok = 0; int fail = 0; int duplicates = 0; long slowest = 0; int accepted = 0; int mixedTotal = 0; int transitionTotal = 0;
            var signatures = new HashSet<string>();
            var failures = new Dictionary<GenerationFailure, int>();
            for (int i = 0; i < count; i++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew(); LevelDataSO data; List<LogicalMove> path; GenerationFailure reason;
                bool success = TryGenerate(startingLevelIndex + i, capacity, NewSeed(), out data, out path, out reason);
                sw.Stop(); slowest = Math.Max(slowest, sw.ElapsedMilliseconds);
                if (success)
                {
                    ok++; accepted += path.Count;
                    if (!signatures.Add(data.puzzleSignature)) duplicates++;
                    int mixed, transitions; GetMetrics(data, out mixed, out transitions); mixedTotal += mixed; transitionTotal += transitions;
                }
                else { fail++; failures[reason] = failures.ContainsKey(reason) ? failures[reason] + 1 : 1; }
            }
            Debug.Log($"[ProceduralLevelGenerator] Batch {count}: success={ok}, failure={fail}, duplicates={duplicates}, avgMs={(double)watch.ElapsedMilliseconds / Math.Max(1, count):F2}, slowestMs={slowest}, avgAcceptedSteps={(double)accepted / Math.Max(1, ok):F1}, avgGuaranteedPath={(double)accepted / Math.Max(1, ok):F1}, avgMixed={(double)mixedTotal / Math.Max(1, ok):F1}, avgTransitions={(double)transitionTotal / Math.Max(1, ok):F1}, failures={FormatFailures(failures)}");
        }

        private LevelDataSO GenerateAndSave(int level, int capacity, int seed)
        {
            LevelDataSO data; List<LogicalMove> path; GenerationFailure reason = GenerationFailure.None;
            int candidateSeed = seed;
            for (int retry = 0; retry < maximumGenerationAttempts; retry++, candidateSeed = NextSeed(candidateSeed))
            {
                if (TryGenerate(level, capacity, candidateSeed, out data, out path, out reason))
                {
                    SaveAccepted(level, candidateSeed, data, path);
                    return currentSnapshot.DeepCopy();
                }
            }
            Debug.LogError($"[ProceduralLevelGenerator] Generation failed at level {level}: {reason}. Trying safe fallback.");
            var fallback = new DifficultyTier { name = "Safe Fallback", minLevel = level, maxLevel = level, activeColorCount = Math.Min(3, supportedColors.Count), emptyBoltCount = 2, targetInverseSteps = 18, minimumAcceptedSteps = 8, minimumMixedBolts = 1, minimumColorTransitions = 2, maximumCompletedBoltsAtStart = 1, minimumGuaranteedSolutionLength = 8, maxGenerationAttempts = 80 };
            if (TryGenerate(level, capacity, candidateSeed, out data, out path, out reason, fallback))
            {
                SaveAccepted(level, candidateSeed, data, path);
                return currentSnapshot.DeepCopy();
            }
            Debug.LogError("[ProceduralLevelGenerator] Safe fallback failed; no invalid board will be loaded.");
            return null;
        }

        private void SaveAccepted(int level, int seed, LevelDataSO data, List<LogicalMove> path)
        {
            currentLevelIndex = level; currentSeed = seed; currentSnapshot = data.DeepCopy();
            // Keep the lightweight path for this session so Inspector replay always remains available.
            // The Inspector flag controls whether it is emitted in debug logging / future save payloads.
            currentSolution = new List<LogicalMove>(path);
            RememberSignature(data.puzzleSignature);
            if (enableDebugLogging) Debug.Log($"[ProceduralLevelGenerator] level={level} seed={seed} v={generatorVersion} tier={data.difficultyTier} path={(storeGuaranteedSolutionPath ? path.Count.ToString() : "not stored")} signature={data.puzzleSignature}");
        }

        private bool TryGenerate(int level, int capacity, int seed, out LevelDataSO data, out List<LogicalMove> forwardPath, out GenerationFailure failure, DifficultyTier overrideTier = null)
        {
            data = null; forwardPath = new List<LogicalMove>(); failure = GenerationFailure.None;
            DifficultyTier tier = overrideTier ?? SelectTier(level);
            if (tier == null || capacity < 1 || tier.emptyBoltCount < 1 || tier.activeColorCount > supportedColors.Count) { failure = GenerationFailure.InvalidConfiguration; return false; }
            var random = new System.Random(seed);
            for (int attempt = 0; attempt < Math.Min(maximumGenerationAttempts, Math.Max(1, tier.maxGenerationAttempts)); attempt++)
            {
                var colors = PickColors(random, tier.activeColorCount);
                var board = CreateSolvedBoard(colors, tier.emptyBoltCount, capacity, random);
                var inverse = new List<LogicalMove>();
                LogicalMove previous = new LogicalMove { sourceBoltIndex = -1, destinationBoltIndex = -1 };
                for (int step = 0; step < tier.targetInverseSteps; step++)
                {
                    LogicalMove accepted;
                    if (!TryInverseStep(board, capacity, tier.emptyBoltCount, random, previous, out accepted)) continue;
                    inverse.Add(accepted); previous = accepted;
                }
                if (inverse.Count < tier.minimumAcceptedSteps) { failure = GenerationFailure.InsufficientAcceptedSteps; continue; }
                forwardPath = ReverseToForward(inverse);
                if (!Replay(board, forwardPath, capacity) || !IsSolved(ReplayState(board, forwardPath, capacity), capacity)) { failure = GenerationFailure.SolutionReplayFailed; continue; }
                GenerationFailure quality = ValidateQuality(board, colors, tier, capacity, forwardPath.Count);
                if (quality != GenerationFailure.None) { failure = quality; continue; }
                string signature = Signature(board, colors);
                if (recentSignatures.Contains(signature)) { failure = GenerationFailure.DuplicateSignature; continue; }
                data = ToLevelData(level, seed, tier, colors, board, signature);
                return true;
            }
            return false;
        }

        private bool TryInverseStep(List<List<NutColor>> board, int capacity, int expectedEmptyBolts, System.Random random, LogicalMove previous, out LogicalMove accepted)
        {
            accepted = new LogicalMove();
            for (int tries = 0; tries < maximumInverseMoveAttemptsPerStep; tries++)
            {
                int from = random.Next(board.Count), to = random.Next(board.Count);
                if (from == to || board[from].Count == 0 || board[to].Count >= capacity) continue;
                int group = TopGroupCount(board[from]);
                int count = random.Next(1, Math.Min(group, capacity - board[to].Count) + 1);
                NutColor color = Top(board[from]);
                // Avoid immediate two-bolt ping-pong of the same group.
                if (previous.sourceBoltIndex == to && previous.destinationBoltIndex == from && previous.color == color) continue;
                var before = Clone(board);
                InverseTransfer(board, from, to, count);
                // Temporary use of an empty bolt is allowed, but construction may never create
                // more maneuvering spaces than the configured board format.
                if (CountEmpty(board) > expectedEmptyBolts) { Copy(before, board); continue; }
                var undo = new LogicalMove { sourceBoltIndex = to, destinationBoltIndex = from, color = color, count = count };
                var check = Clone(board);
                if (ApplyForward(check, undo, capacity) == count && Equal(check, before))
                {
                    accepted = new LogicalMove { sourceBoltIndex = from, destinationBoltIndex = to, color = color, count = count };
                    return true;
                }
                Copy(before, board);
            }
            return false;
        }

        private static void InverseTransfer(List<List<NutColor>> b, int from, int to, int count)
        {
            var source = b[from]; var dest = b[to]; int first = source.Count - count;
            for (int i = first; i < source.Count; i++) dest.Add(source[i]);
            source.RemoveRange(first, count);
        }

        // This is the pure counterpart of GameManager.GetMoveCount: same group, compatibility and partial-capacity rule.
        private static int ApplyForward(List<List<NutColor>> b, LogicalMove move, int capacity)
        {
            var source = b[move.sourceBoltIndex]; var destination = b[move.destinationBoltIndex];
            if (source.Count == 0 || destination.Count >= capacity) return 0;
            NutColor color = Top(source);
            if (destination.Count > 0 && Top(destination) != color) return 0;
            int count = Math.Min(TopGroupCount(source), capacity - destination.Count);
            if (count <= 0) return 0;
            InverseTransfer(b, move.sourceBoltIndex, move.destinationBoltIndex, count);
            return count;
        }

        private GenerationFailure ValidateQuality(List<List<NutColor>> b, List<NutColor> colors, DifficultyTier tier, int cap, int pathLength)
        {
            if (!ValidateStructure(b, colors, tier.emptyBoltCount, cap)) return GenerationFailure.InvalidColorCount;
            if (IsSolved(b, cap)) return GenerationFailure.PuzzleAlreadySolved;
            int mixed = 0, transitions = 0, completed = 0, legal = CountLegalMoves(b, cap);
            foreach (var bolt in b)
            {
                if (bolt.Count == cap && IsUniform(bolt)) completed++;
                bool isMixed = false;
                for (int i = 1; i < bolt.Count; i++) if (bolt[i] != bolt[i - 1]) { transitions++; isMixed = true; }
                if (isMixed) mixed++;
            }
            if (mixed < tier.minimumMixedBolts) return GenerationFailure.InsufficientMixedBolts;
            if (transitions < tier.minimumColorTransitions) return GenerationFailure.InsufficientTransitions;
            if (completed > tier.maximumCompletedBoltsAtStart) return GenerationFailure.TooManyCompletedBolts;
            if (pathLength < tier.minimumGuaranteedSolutionLength) return GenerationFailure.InsufficientAcceptedSteps;
            if (legal < tier.minimumStartingLegalMoves || (tier.maximumStartingLegalMoves > 0 && legal > tier.maximumStartingLegalMoves)) return GenerationFailure.NoStartingLegalMoves;
            if (OneMoveFromSolved(b, cap)) return GenerationFailure.PuzzleOneMoveFromSolved;
            return GenerationFailure.None;
        }

        private bool ValidateStructure(List<List<NutColor>> b, List<NutColor> colors, int empty, int cap)
        {
            int actualEmpty = 0; var counts = new Dictionary<NutColor, int>(); foreach (var c in colors) counts[c] = 0;
            foreach (var bolt in b) { if (bolt.Count > cap) return false; if (bolt.Count == 0) actualEmpty++; foreach (var c in bolt) { if (!counts.ContainsKey(c)) return false; counts[c]++; } }
            if (actualEmpty != empty) return false;
            foreach (var c in colors) if (counts[c] != cap) return false;
            return true;
        }

        private static bool OneMoveFromSolved(List<List<NutColor>> b, int cap)
        {
            for (int i = 0; i < b.Count; i++) for (int j = 0; j < b.Count; j++) if (i != j)
            {
                var copy = Clone(b); if (ApplyForward(copy, new LogicalMove { sourceBoltIndex = i, destinationBoltIndex = j }, cap) > 0 && IsSolved(copy, cap)) return true;
            }
            return false;
        }
        private static int CountLegalMoves(List<List<NutColor>> b, int cap) { int n = 0; for (int i = 0; i < b.Count; i++) for (int j = 0; j < b.Count; j++) if (i != j && ApplyForward(Clone(b), new LogicalMove { sourceBoltIndex = i, destinationBoltIndex = j }, cap) > 0) n++; return n; }
        private static bool Replay(List<List<NutColor>> b, List<LogicalMove> moves, int cap) { var state = ReplayState(b, moves, cap); return state != null; }
        private static List<List<NutColor>> ReplayState(List<List<NutColor>> b, List<LogicalMove> moves, int cap)
        {
            var state = Clone(b);
            foreach (var m in moves)
            {
                if (m.sourceBoltIndex < 0 || m.sourceBoltIndex >= state.Count || state[m.sourceBoltIndex].Count == 0 ||
                    Top(state[m.sourceBoltIndex]) != m.color || ApplyForward(state, m, cap) != m.count)
                    return null;
            }
            return state;
        }
        private static bool IsSolved(List<List<NutColor>> b, int cap) { if (b == null) return false; foreach (var bolt in b) if (bolt.Count != 0 && (bolt.Count != cap || !IsUniform(bolt))) return false; return true; }
        private static bool IsUniform(List<NutColor> bolt) { for (int i = 1; i < bolt.Count; i++) if (bolt[i] != bolt[0]) return false; return true; }
        private static NutColor Top(List<NutColor> b) => b[b.Count - 1];
        private static int TopGroupCount(List<NutColor> b) { if (b.Count == 0) return 0; NutColor c = Top(b); int n = 0; for (int i = b.Count - 1; i >= 0 && b[i] == c; i--) n++; return n; }
        private static List<List<NutColor>> Clone(List<List<NutColor>> b) { var c = new List<List<NutColor>>(b.Count); foreach (var x in b) c.Add(new List<NutColor>(x)); return c; }
        private static void Copy(List<List<NutColor>> from, List<List<NutColor>> to) { for (int i = 0; i < from.Count; i++) { to[i].Clear(); to[i].AddRange(from[i]); } }
        private static bool Equal(List<List<NutColor>> a, List<List<NutColor>> b) { if (a.Count != b.Count) return false; for (int i = 0; i < a.Count; i++) if (a[i].Count != b[i].Count) return false; else for (int j = 0; j < a[i].Count; j++) if (a[i][j] != b[i][j]) return false; return true; }
        private static List<LogicalMove> ReverseToForward(List<LogicalMove> inverse) { var result = new List<LogicalMove>(inverse.Count); for (int i = inverse.Count - 1; i >= 0; i--) { var m = inverse[i]; result.Add(new LogicalMove { sourceBoltIndex = m.destinationBoltIndex, destinationBoltIndex = m.sourceBoltIndex, color = m.color, count = m.count }); } return result; }

        private List<List<NutColor>> CreateSolvedBoard(List<NutColor> colors, int empty, int cap, System.Random random)
        {
            var board = new List<List<NutColor>>(); foreach (var c in colors) { var bolt = new List<NutColor>(); for (int i = 0; i < cap; i++) bolt.Add(c); board.Add(bolt); } for (int i = 0; i < empty; i++) board.Add(new List<NutColor>());
            for (int i = board.Count - 1; i > 0; i--) { int j = random.Next(i + 1); var temp = board[i]; board[i] = board[j]; board[j] = temp; } return board;
        }
        private List<NutColor> PickColors(System.Random random, int count) { var pool = new List<NutColor>(supportedColors); for (int i = pool.Count - 1; i > 0; i--) { int j = random.Next(i + 1); var t = pool[i]; pool[i] = pool[j]; pool[j] = t; } return pool.GetRange(0, count); }
        private DifficultyTier SelectTier(int level) { foreach (var tier in difficultyTiers) if (tier != null && level >= tier.minLevel && level <= tier.maxLevel) return tier; return difficultyTiers.Count > 0 ? difficultyTiers[difficultyTiers.Count - 1] : null; }
        private LevelDataSO ToLevelData(int level, int seed, DifficultyTier tier, List<NutColor> colors, List<List<NutColor>> board, string signature) { var d = ScriptableObject.CreateInstance<LevelDataSO>(); d.levelNumber = level; d.isProcedural = true; d.seed = seed; d.generatorVersion = generatorVersion; d.difficultyTier = tier.name; d.activeColors = colors.ToArray(); d.puzzleSignature = signature; d.bolts = new List<BoltNutStackData>(); foreach (var bolt in board) d.bolts.Add(new BoltNutStackData { nutColors = bolt.ToArray() }); return d; }
        private string Signature(List<List<NutColor>> board, List<NutColor> colors) { var sb = new StringBuilder("V").Append(generatorVersion).Append('|'); foreach (var c in colors) sb.Append((int)c).Append(','); sb.Append('|'); foreach (var b in board) { if (b.Count == 0) sb.Append('_'); else for (int i = 0; i < b.Count; i++) { if (i > 0) sb.Append(','); sb.Append((int)b[i]); } sb.Append(';'); } return sb.ToString(); }
        private bool ReplaySaved(int cap) { if (currentSnapshot == null) return false; var board = new List<List<NutColor>>(); foreach (var b in currentSnapshot.bolts) board.Add(new List<NutColor>(b.nutColors)); var state = ReplayState(board, currentSolution, cap); return state != null && IsSolved(state, cap); }
        private string ValidateSavedCurrent(int cap) { if (currentSnapshot == null) return "No current snapshot."; var b = new List<List<NutColor>>(); foreach (var x in currentSnapshot.bolts) b.Add(new List<NutColor>(x.nutColors)); return ValidateStructure(b, new List<NutColor>(currentSnapshot.activeColors), CountEmpty(b), cap) && ReplaySaved(cap) ? "Current puzzle is valid." : "Current puzzle failed validation."; }
        private static int CountEmpty(List<List<NutColor>> b) { int n = 0; foreach (var x in b) if (x.Count == 0) n++; return n; }
        private static void GetMetrics(LevelDataSO data, out int mixed, out int transitions)
        {
            mixed = 0; transitions = 0;
            foreach (var stack in data.bolts)
            {
                bool isMixed = false; var nuts = stack.nutColors;
                for (int i = 1; i < nuts.Length; i++) if (nuts[i] != nuts[i - 1]) { transitions++; isMixed = true; }
                if (isMixed) mixed++;
            }
        }
        private void RememberSignature(string signature) { recentSignatures.Enqueue(signature); while (recentSignatures.Count > recentSignatureHistorySize) recentSignatures.Dequeue(); }
        private static int NewSeed() => unchecked((int)DateTime.UtcNow.Ticks ^ Environment.TickCount);
        private static int NextSeed(int seed) => unchecked(seed * 1103515245 + 12345);
        private static string FormatFailures(Dictionary<GenerationFailure, int> f) { var s = new StringBuilder(); foreach (var x in f) s.Append(x.Key).Append('=').Append(x.Value).Append(' '); return s.ToString(); }
    }
}
