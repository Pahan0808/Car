using UnityEngine;

namespace DriveMad
{
    /// <summary>
    /// All tuning values for the car. Scene references (chassis, axles, joints) stay on the
    /// DriveMadCarController component, because they are per-instance wiring, not settings.
    /// </summary>
    [CreateAssetMenu(menuName = "Drive Mad/Car Settings", fileName = "CarSettings")]
    public class CarSettings : ScriptableObject
    {
        [Header("Body")]
        public float mass = 200f;
        public Vector3 centerOfMass = new Vector3(0f, 0.08f, -0.16f);
        public float linearDamping = 0.04f;
        public float angularDamping = 0.22f;

        [Header("Axles")]
        public float bottomMass = 8f;
        public float wheelMass = 16f;
        public float wheelRadius = 0.34f;

        [Header("Suspension")]
        [Tooltip("Off = keep the ConfigurableJoint settings authored in the scene; only connectedBody is wired.")]
        public bool autoConfigureJoints = true;
        [Tooltip("Compression / extension travel from the authored ride height (symmetric).")]
        public float suspensionTravel = 0.07f;
        [Tooltip("Gap between chassis box bottom and wheel top at full compression.")]
        public float bodyWheelClearance = 0.03f;
        public float suspensionSpring = 36000f;
        [Tooltip("Absolute ceiling for the damper coefficient.")]
        public float suspensionDamper = 2200f;
        [Tooltip("1 = critically damped, >1 = overdamped. Solved implicitly, so high values stay stable.")]
        public float suspensionDampingRatio = 2f;
        public float maxSuspensionForce = 6000f;
        [Tooltip("Share of the spring reaction applied at the axle mount; the rest goes to the center of mass. Lower = less body rocking.")]
        [Range(0f, 1f)]
        public float suspensionPitchTransfer = 0.6f;
        [Tooltip("Move the center of mass to the midpoint between the axle mounts so both springs carry the same load.")]
        public bool autoBalanceCenterOfMass = true;

        [Header("Motor / grip")]
        public float maxWheelSpin = 90f;
        [Tooltip("Slip stiffness: traction acceleration per m/s of wheel-vs-ground slip.")]
        public float longitudinalGrip = 8f;
        [Tooltip("Traction acceleration cap per axle (m/s^2). Both axles are driven.")]
        public float maxTractionAccel = 12f;
        public float maxSpeed = 15f;
        [Tooltip("Extra pitch torque while grounded. Keep small: the wheelie comes from real traction.")]
        public float wheelieAssist = 0.12f;
        public float airPitchTorque = 6f;
        public float airAngularDamping = 0.1f;
        [Tooltip("Local spin axis on the ColliderWheels Rigidbody; child meshes follow automatically.")]
        public Vector3 wheelSpinAxis = Vector3.up;
        [Tooltip("PhysX friction on the wheel colliders. Traction itself is solved by the slip model.")]
        [Range(0f, 1f)]
        public float wheelFriction = 0.35f;
        [Tooltip("Wheel spin decay per second while coasting.")]
        public float rollingResistance = 0.8f;

        [Header("Crash")]
        [Tooltip("Body tilt from upright, in degrees, at which the car counts as rolled over.")]
        [Range(10f, 180f)]
        public float upsideDownAngle = 80f;

        [Header("Collision")]
        public LayerMask groundMask = 1 << 8;
    }
}
