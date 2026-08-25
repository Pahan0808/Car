using UnityEngine;

namespace DriveMad
{
    /// <summary>
    /// Level / session rules: world physics, fail conditions and status messages.
    /// </summary>
    [CreateAssetMenu(menuName = "Drive Mad/Game Settings", fileName = "GameSettings")]
    public class GameSettings : ScriptableObject
    {
        [Header("World")]
        public Vector3 gravity = new Vector3(0f, -12.5f, 0f);
        [Tooltip("Ground layer index. The body and the wheels must collide with it.")]
        public int groundLayer = 8;
        [Tooltip("Vehicle layer index.")]
        public int vehicleLayer = 9;

        [Header("Fail conditions")]
        [Tooltip("How long the car has to stay rolled over before it counts as a crash.")]
        public float upsideDownFailTime = 0.55f;
        [Tooltip("Below this world height the run counts as a fall.")]
        public float fallY = -12f;

        [Header("Status messages")]
        public string winMessage = "FINISH";
        public string crashMessage = "CRASH — R / Space";
        public string fellMessage = "FELL — R / Space";
    }
}
