using UnityEngine;
using NutBoltSort;

/// <summary>
/// Legacy wrapper for NutBoltSortPrototype. Delegated to GameManager and LevelManager.
/// </summary>
public sealed class NutBoltSortPrototype : MonoBehaviour
{
    private GameManager gameManager;

    private void Awake()
    {
        gameManager = GetComponent<GameManager>();
        if (gameManager == null)
        {
            gameManager = gameObject.AddComponent<GameManager>();
        }
    }
}
