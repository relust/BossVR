using UnityEngine;

public class FightCamera : MonoBehaviour
{
    private Camera _camera;
    private Vector3 _fixedRotation;
    private Damageable[] _targets;

    [SerializeField] private float _minDistance = 5f;
    [SerializeField] private float _padding = 2f;
    [SerializeField] private float _smoothSpeed = 5f;

    private void Awake()
    {
        _camera = GetComponent<Camera>();
        _fixedRotation = transform.eulerAngles;
    }

    private void Start()
    {
        _targets = FindObjectsByType<Damageable>(FindObjectsSortMode.None);
    }

    private void LateUpdate()
    {
        if (_targets == null || _targets.Length == 0) return;

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minZ = float.MaxValue;
        float maxZ = float.MinValue;

        int activeTargets = 0;

        for (int i = 0; i < _targets.Length; i++)
        {
            if (_targets[i] != null)
            {
                Vector3 pos = _targets[i].transform.position;
                if (pos.x < minX) minX = pos.x;
                if (pos.x > maxX) maxX = pos.x;
                if (pos.z < minZ) minZ = pos.z;
                if (pos.z > maxZ) maxZ = pos.z;
                activeTargets++;
            }
        }

        if (activeTargets == 0) return;

        Vector3 center = new Vector3((minX + maxX) / 2f, 0, (minZ + maxZ) / 2f);

        Quaternion camRot = Quaternion.Euler(_fixedRotation);
        Quaternion invCamRot = Quaternion.Inverse(camRot);

        float tanV = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float tanH = tanV * _camera.aspect;

        float requiredDistance = _minDistance;

        for (int i = 0; i < _targets.Length; i++)
        {
            if (_targets[i] != null)
            {
                Vector3 localP = invCamRot * (_targets[i].transform.position - center);
                
                // To fit in vertical FOV with padding
                float distV = (Mathf.Abs(localP.y) + _padding) / tanV - localP.z;
                // To fit in horizontal FOV with padding
                float distH = (Mathf.Abs(localP.x) + _padding) / tanH - localP.z;
                
                requiredDistance = Mathf.Max(requiredDistance, distV, distH);
            }
        }

        Vector3 targetPosition = center - (camRot * Vector3.forward) * requiredDistance;

        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * _smoothSpeed);
        transform.eulerAngles = _fixedRotation;
    }
}
