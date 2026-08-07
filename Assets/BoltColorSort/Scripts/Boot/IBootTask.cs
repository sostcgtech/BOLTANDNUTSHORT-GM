using System.Collections;

namespace NutBoltSort
{
    /// <summary>
    /// Contract for a single atomic startup task executed by GameBootstrap.
    ///
    /// Each task is responsible for:
    ///   - Providing a short human-readable status string shown on the splash screen.
    ///   - Running its initialization work inside Execute() as a coroutine.
    ///   - Reporting success or failure via <see cref="Succeeded"/> after Execute() completes.
    ///
    /// Tasks must never throw unhandled exceptions; catch internally and set Succeeded = false.
    /// </summary>
    public interface IBootTask
    {
        /// <summary>Short human-readable text displayed while this task is running.</summary>
        string StatusText { get; }

        /// <summary>
        /// True if the task completed without a critical error.
        /// Set this before the Execute coroutine yields its final step.
        /// </summary>
        bool Succeeded { get; }

        /// <summary>Executes the task. Called once by GameBootstrap in sequence.</summary>
        IEnumerator Execute();
    }
}
