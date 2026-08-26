using System;
using UnityEngine;
using UnityEngine.SceneManagement;

[Serializable]
public class CharacterType
{
    [SerializeField] private string _name;
    [SerializeField] private GameObject _prefab;

    public string Name => _name;
    public GameObject Prefab => _prefab;
}

[CreateAssetMenu(fileName = "CharacterManager", menuName = "BossVR/CharacterManager")]
public class CharacterManager : ScriptableObject
{
    [SerializeField] private ControllerManager _controllerManager;
    [SerializeField] private CharacterType[] _characterTypes;
    [SerializeField] private string _defaultCharacterName = "Knight";

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "Fight")
        {
            SpawnCharacters();
        }
    }

    private void SpawnCharacters()
    {
        GameObject defaultPrefab = null;
        foreach (var charType in _characterTypes)
        {
            if (charType.Name == _defaultCharacterName)
            {
                defaultPrefab = charType.Prefab;
                break;
            }
        }

        foreach (var controller in _controllerManager.GetControllers())
        {
            GameObject characterInstance = Instantiate(defaultPrefab);
            AbstractPlayer player = characterInstance.GetComponent<AbstractPlayer>();
            player.SetController(controller);
        }
    }
}
