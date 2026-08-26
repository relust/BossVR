using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "ControllerManager", menuName = "BossVR/ControllerManager")]
public class ControllerManager : ScriptableObject
{
    private bool _debugMode = true;
    private List<AbstractController> _controllers = new List<AbstractController>();

    private void OnEnable()
    {
        if (_debugMode)
        {
            _controllers.Clear();
            _controllers.Add(new KeyboardController());
        }
    }

    public List<AbstractController> GetControllers()
    {
        return _controllers;
    }
}
