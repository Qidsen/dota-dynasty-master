#nullable enable
using UnityEngine;

namespace Dynasty.Unity
{
    /// <summary>
    /// Observer camera over the board: free (pan with WASD/arrows or by dragging the right mouse
    /// button, zoom with the wheel) or following a unit. A fixed broadcast-style pitch.
    /// </summary>
    public sealed class BroadcastCamera
    {
        private const float Pitch = 55f;
        private const float MinDistance = 12f;
        private const float MaxDistance = 110f;

        private readonly Camera _camera;
        private Vector3 _focus;
        private float _distance = 45f;

        public BroadcastCamera(Vector3 focus)
        {
            var go = new GameObject("Broadcast Camera");
            _camera = go.AddComponent<Camera>();
            _camera.fieldOfView = 40f;
            _camera.nearClipPlane = 0.3f;
            _camera.farClipPlane = 500f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
            go.tag = "MainCamera";
            _focus = focus;
            Apply();
        }

        public Camera Camera => _camera;

        /// <summary>The unit being followed, or null for the free camera.</summary>
        public long? Following { get; set; }

        public void Update(WorldView world, float mapSize, bool acceptInput)
        {
            if (acceptInput)
                ReadInput(mapSize);
            if (Following is long id && world.TryGetPosition(id, out Vector3 target))
                _focus = Vector3.Lerp(_focus, new Vector3(target.x, 0f, target.z), 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
            Apply();
        }

        public void LookAt(Vector3 point)
        {
            _focus = new Vector3(point.x, 0f, point.z);
            Apply();
        }

        private void ReadInput(float mapSize)
        {
            float dt = Time.unscaledDeltaTime;
            var move = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            if (Input.GetMouseButton(1))
                move += new Vector3(-Input.GetAxis("Mouse X"), 0f, -Input.GetAxis("Mouse Y")) * 6f;
            if (move.sqrMagnitude > 0.0001f)
            {
                Following = null;
                Quaternion yaw = Quaternion.Euler(0f, _camera.transform.eulerAngles.y, 0f);
                _focus += yaw * move * (_distance * 0.9f * dt);
            }

            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f)
                _distance = Mathf.Clamp(_distance * Mathf.Pow(0.88f, wheel), MinDistance, MaxDistance);

            _focus.x = Mathf.Clamp(_focus.x, 0f, mapSize);
            _focus.z = Mathf.Clamp(_focus.z, 0f, mapSize);
        }

        private void Apply()
        {
            // Looks "up" the map from the Dawn corner, as the board is laid out diagonally.
            Quaternion rotation = Quaternion.Euler(Pitch, 45f, 0f);
            _camera.transform.rotation = rotation;
            _camera.transform.position = _focus - rotation * Vector3.forward * _distance;
        }
    }
}
