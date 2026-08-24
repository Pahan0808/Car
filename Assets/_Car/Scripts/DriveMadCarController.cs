using UnityEngine;

namespace DriveMad
{
    /// <summary>
    /// SUV-style suspension: wheels on ground, body on springs (Top on chassis, Bottom = spring foot).
    /// Symmetric travel around ride height; fully compressed = body barely clears wheels.
    /// </summary>
    public class DriveMadCarController : MonoBehaviour
    {
        [System.Serializable]
        public class AxleSetup
        {
            public string name;
            public Transform suspensionParent;
            public Transform top;
            public Transform bottom;
            public Transform wheelCollider;
            public Transform leftVisual;
            public Transform rightVisual;
            public Transform suspensionVisual;
            [Tooltip("Suspension joint on Bottom (Bottom -> ColliiderBody).")]
            public ConfigurableJoint suspensionJoint;
            [Tooltip("Wheel joint on ColliderWheels (ColliderWheels -> Bottom).")]
            public ConfigurableJoint wheelJoint;

            [System.NonSerialized] public Rigidbody bottomBody;
            [System.NonSerialized] public Rigidbody wheelBody;
            [System.NonSerialized] public Collider wheelCol;
            [System.NonSerialized] public float radius;
            [System.NonSerialized] public Vector3 topLocalOnChassis;
            [System.NonSerialized] public Vector3 wheelLocalOnBottom;
            [System.NonSerialized] public Quaternion wheelLocalRotOnBottom;
            [System.NonSerialized] public float minSpringLength;
            [System.NonSerialized] public float restSpringLength;
            [System.NonSerialized] public float maxSpringLength;
            [System.NonSerialized] public float groundY;
        }

        [Header("Physics")]
        [SerializeField] Transform chassisPhysics;
        [SerializeField] float mass = 200f;
        [SerializeField] Vector3 centerOfMass = new Vector3(0f, 0f, 0f);
        [SerializeField] float linearDamping = 0.04f;
        [SerializeField] float angularDamping = 0.22f;

        [Header("Axles")]
        [SerializeField] AxleSetup front = new AxleSetup { name = "Front" };
        [SerializeField] AxleSetup rear = new AxleSetup { name = "Rear" };
        [SerializeField] float bottomMass = 4f;
        [SerializeField] float wheelMass = 24f;
        [SerializeField] float wheelRadius = 0.34f;

        [Header("Suspension")]
        [Tooltip("Off = keep your ConfigurableJoint settings; only connectedBody is wired.")]
        [SerializeField] bool autoConfigureJoints = true;
        [Tooltip("Compression / extension travel from ride height (symmetric).")]
        [SerializeField] float suspensionTravel = 0.12f;
        [Tooltip("Gap between chassis box bottom and wheel top at full compression.")]
        [SerializeField] float bodyWheelClearance = 0.03f;
        [SerializeField] float suspensionSpring = 28000f;
        [SerializeField] float suspensionDamper = 3200f;
        [SerializeField] float maxSuspensionForce = 6000f;

        [Header("Motor / grip")]
        [SerializeField] float maxWheelSpin = 90f;
        [SerializeField] float longitudinalGrip = 22f;
        [SerializeField] float maxSpeed = 18f;
        [SerializeField] float wheelieAssist = 0.1f;
        [SerializeField] float airPitchTorque = 5f;
        [SerializeField] float airAngularDamping = 0.1f;
        [Tooltip("Local spin axis on ColliderWheels Rigidbody; child meshes follow automatically.")]
        [SerializeField] Vector3 wheelSpinAxis = Vector3.up;

        [Header("Collision")]
        [SerializeField] LayerMask groundMask = 1 << 8;
        const int WheelPhysicsLayer = 0;

        Rigidbody _chassis;
        AxleSetup[] _axles;
        Collider[] _chassisColliders;
        float _bodyBottomLocalY;
        float _throttle;
        bool _physicsFrozen;

        public float Throttle => _throttle;
        public bool IsGrounded { get; private set; }
        public bool IsUpsideDown { get; private set; }
        public Rigidbody Body => _chassis;
        public Transform Chassis => chassisPhysics != null ? chassisPhysics : transform;

        public void SetThrottle(float value) => _throttle = Mathf.Clamp(value, -1f, 1f);
        public void SetChassis(Transform value) => chassisPhysics = value;

        public void SetVisuals(Transform leftFront, Transform rightFront, Transform leftRear, Transform rightRear,
            Transform frontSuspension, Transform rearSuspension)
        {
            front.leftVisual = leftFront;
            front.rightVisual = rightFront;
            front.suspensionVisual = frontSuspension;
            rear.leftVisual = leftRear;
            rear.rightVisual = rightRear;
            rear.suspensionVisual = rearSuspension;
        }

        public void FreezePhysics(bool freeze)
        {
            _physicsFrozen = freeze;
            ApplyKinematic(freeze);
        }

        void Awake()
        {
            ResolveChassis();
            _axles = new[] { BindAxle(front), BindAxle(rear) };
        }

        void Start()
        {
            AlignVehicleToGround();
            Physics.SyncTransforms();
        }

        void ResolveChassis()
        {
            if (chassisPhysics == null)
            {
                chassisPhysics = transform.Find("ColliiderBody");
                if (chassisPhysics == null)
                {
                    chassisPhysics = transform.Find("ColliderBody");
                }
            }

            if (chassisPhysics == null)
            {
                Debug.LogError("DriveMad: assign ColliiderBody.", this);
                return;
            }

            _chassis = chassisPhysics.GetComponent<Rigidbody>();
            if (_chassis == null)
            {
                _chassis = chassisPhysics.gameObject.AddComponent<Rigidbody>();
            }

            _chassis.mass = mass;
            _chassis.useGravity = true;
            _chassis.linearDamping = linearDamping;
            _chassis.angularDamping = angularDamping;
            _chassis.interpolation = RigidbodyInterpolation.Interpolate;
            _chassis.collisionDetectionMode = CollisionDetectionMode.Continuous;
            // Side-view vehicle: only pitch (world X) may rotate, roll and yaw stay locked.
            _chassis.constraints = RigidbodyConstraints.FreezePositionX
                                   | RigidbodyConstraints.FreezeRotationY
                                   | RigidbodyConstraints.FreezeRotationZ;
            _chassis.centerOfMass = centerOfMass;
            _chassis.maxAngularVelocity = 16f;

            _chassisColliders = chassisPhysics.GetComponentsInChildren<Collider>(true);
            _bodyBottomLocalY = GetBodyBottomLocalY();
        }

        float GetBodyBottomLocalY()
        {
            BoxCollider box = chassisPhysics.GetComponent<BoxCollider>();
            if (box != null)
            {
                return box.center.y - box.size.y * 0.5f;
            }

            Bounds b = new Bounds(Vector3.zero, Vector3.zero);
            bool hasBounds = false;
            Collider[] cols = chassisPhysics.GetComponents<Collider>();
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] == null || cols[i].isTrigger)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    b = cols[i].bounds;
                    hasBounds = true;
                }
                else
                {
                    b.Encapsulate(cols[i].bounds);
                }
            }

            if (!hasBounds)
            {
                return 0.1f;
            }

            Vector3 localMin = chassisPhysics.InverseTransformPoint(new Vector3(b.min.x, b.min.y, b.min.z));
            return localMin.y;
        }

        AxleSetup BindAxle(AxleSetup axle)
        {
            if (axle.bottom == null || axle.wheelCollider == null || _chassis == null)
            {
                Debug.LogWarning($"DriveMad: incomplete axle {axle.name}.", this);
                return axle;
            }

            if (axle.top == null && axle.suspensionParent != null)
            {
                axle.top = axle.suspensionParent.Find("Top");
            }

            if (axle.suspensionParent != null && axle.suspensionParent.parent == chassisPhysics)
            {
                axle.suspensionParent.SetParent(transform, true);
            }

            if (axle.suspensionVisual == null && axle.bottom != null)
            {
                axle.suspensionVisual = axle.bottom.Find("Visual");
            }

            ResolveWheelVisuals(axle);

            if (axle.wheelCollider.parent == axle.bottom)
            {
                axle.wheelCollider.SetParent(transform, true);
            }

            if (axle.top != null)
            {
                axle.topLocalOnChassis = chassisPhysics.InverseTransformPoint(axle.top.position);
                axle.top.SetParent(chassisPhysics, true);
                axle.top.localPosition = axle.topLocalOnChassis;
                axle.top.localRotation = Quaternion.identity;
            }
            else if (axle.suspensionParent != null)
            {
                axle.topLocalOnChassis = chassisPhysics.InverseTransformPoint(axle.suspensionParent.position);
            }
            else
            {
                axle.topLocalOnChassis = axle.bottom.localPosition;
            }

            axle.wheelLocalOnBottom = axle.bottom.InverseTransformPoint(axle.wheelCollider.position);
            axle.wheelLocalRotOnBottom = Quaternion.Inverse(axle.bottom.rotation) * axle.wheelCollider.rotation;

            SetupBottomBody(axle);
            SetupWheelBody(axle);
            ComputeSpringRange(axle);
            SetupJoints(axle);

            return axle;
        }

        static void ResolveWheelVisuals(AxleSetup axle)
        {
            if (axle.leftVisual != null && axle.rightVisual != null)
            {
                return;
            }

            Transform wRoot = axle.wheelCollider;
            for (int i = 0; i < wRoot.childCount; i++)
            {
                Transform c = wRoot.GetChild(i);
                if (c.name.Contains("FL") || c.name.Contains("RL") || c.name.Contains("L"))
                {
                    axle.leftVisual = c;
                }
                else if (c.name.Contains("FR") || c.name.Contains("RR") || c.name.Contains("R"))
                {
                    axle.rightVisual = c;
                }
            }
        }

        void SetupBottomBody(AxleSetup axle)
        {
            axle.bottomBody = axle.bottom.GetComponent<Rigidbody>();
            if (axle.bottomBody == null)
            {
                axle.bottomBody = axle.bottom.gameObject.AddComponent<Rigidbody>();
            }

            axle.bottomBody.mass = bottomMass;
            axle.bottomBody.useGravity = true;
            axle.bottomBody.interpolation = RigidbodyInterpolation.Interpolate;
            axle.bottomBody.collisionDetectionMode = CollisionDetectionMode.Continuous;
            // Pitch must stay free, otherwise the suspension joint chains the chassis rotation
            // to a world-locked Bottom and the body can never tilt on slopes.
            axle.bottomBody.constraints = RigidbodyConstraints.FreezePositionX
                                          | RigidbodyConstraints.FreezeRotationY
                                          | RigidbodyConstraints.FreezeRotationZ;
            axle.bottomBody.maxAngularVelocity = 16f;
            axle.bottomBody.linearDamping = 0.05f;
            axle.bottomBody.angularDamping = 0.5f;
        }

        void SetupWheelBody(AxleSetup axle)
        {
            axle.wheelBody = axle.wheelCollider.GetComponent<Rigidbody>();
            if (axle.wheelBody == null)
            {
                axle.wheelBody = axle.wheelCollider.gameObject.AddComponent<Rigidbody>();
            }

            axle.wheelBody.mass = wheelMass;
            axle.wheelBody.useGravity = true;
            axle.wheelBody.interpolation = RigidbodyInterpolation.Interpolate;
            axle.wheelBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            // Keep the wheel on its lateral rail only. Rotation is restricted by the wheel joint
            // (angularX free around wheelSpinAxis), so world-space rotation locks would fight the
            // joint as soon as the suspension pitches.
            axle.wheelBody.constraints = RigidbodyConstraints.FreezePositionX;
            axle.wheelBody.maxAngularVelocity = 200f;
            axle.wheelBody.linearDamping = 0.02f;
            axle.wheelBody.angularDamping = 0.05f;

            EnsureWheelCollider(axle);
            SetLayerRecursive(axle.wheelCollider, WheelPhysicsLayer);
            IgnoreChassisCollision(axle.wheelCol);
        }

        RigidbodyConstraints GetWheelRotationConstraints()
        {
            Vector3 spin = wheelSpinAxis.sqrMagnitude > 0.0001f ? wheelSpinAxis.normalized : Vector3.up;
            float ax = Mathf.Abs(spin.x);
            float ay = Mathf.Abs(spin.y);
            float az = Mathf.Abs(spin.z);

            if (ay >= ax && ay >= az)
            {
                return RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            }

            if (ax >= az)
            {
                return RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ;
            }

            return RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY;
        }

        void EnsureWheelCollider(AxleSetup axle)
        {
            foreach (Collider col in axle.wheelCollider.GetComponents<Collider>())
            {
                if (col is MeshCollider)
                {
                    col.enabled = false;
                }
            }

            SphereCollider sphere = axle.wheelCollider.GetComponent<SphereCollider>();
            if (sphere == null)
            {
                sphere = axle.wheelCollider.gameObject.AddComponent<SphereCollider>();
            }

            sphere.enabled = true;
            sphere.isTrigger = false;
            axle.wheelCol = sphere;

            axle.radius = MeasureRadius(sphere);
            if (axle.radius < 0.05f)
            {
                axle.radius = wheelRadius;
            }

            sphere.radius = axle.radius;
            sphere.center = Vector3.zero;
        }

        static void SetLayerRecursive(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
            {
                SetLayerRecursive(root.GetChild(i), layer);
            }
        }

        void ComputeSpringRange(AxleSetup axle)
        {
            float mountLocalY = axle.topLocalOnChassis.y;
            float wheelCenterAboveBottom = axle.wheelLocalOnBottom.y;
            float wheelTopAboveBottom = wheelCenterAboveBottom + axle.radius;

            // Fully compressed: body box bottom sits bodyWheelClearance above wheel tops.
            float chassisYAtFullCompress = axle.radius * 2f + bodyWheelClearance - _bodyBottomLocalY;
            float bottomYAtFullCompress = axle.radius - wheelCenterAboveBottom;
            float minLength = (mountLocalY + chassisYAtFullCompress) - bottomYAtFullCompress;
            minLength = Mathf.Max(0.08f, minLength);

            axle.minSpringLength = minLength;
            axle.restSpringLength = minLength + suspensionTravel;
            axle.maxSpringLength = minLength + suspensionTravel * 2f;
        }

        void SetupJoints(AxleSetup axle)
        {
            if (axle.suspensionJoint == null)
            {
                axle.suspensionJoint = axle.bottom.GetComponent<ConfigurableJoint>();
            }

            if (axle.suspensionJoint == null)
            {
                axle.suspensionJoint = axle.bottom.gameObject.AddComponent<ConfigurableJoint>();
            }

            if (axle.wheelJoint == null)
            {
                axle.wheelJoint = axle.wheelCollider.GetComponent<ConfigurableJoint>();
            }

            if (axle.wheelJoint == null)
            {
                axle.wheelJoint = axle.wheelCollider.gameObject.AddComponent<ConfigurableJoint>();
            }

            axle.suspensionJoint.connectedBody = _chassis;
            axle.wheelJoint.connectedBody = axle.bottomBody;

            if (autoConfigureJoints)
            {
                ConfigureSuspensionJoint(axle);
                ConfigureWheelJoint(axle);
            }
        }

        void IgnoreChassisCollision(Collider wheelCol)
        {
            if (_chassisColliders == null || wheelCol == null)
            {
                return;
            }

            for (int i = 0; i < _chassisColliders.Length; i++)
            {
                Collider c = _chassisColliders[i];
                if (c != null && c != wheelCol)
                {
                    Physics.IgnoreCollision(wheelCol, c, true);
                }
            }
        }

        void ConfigureSuspensionJoint(AxleSetup axle)
        {
            ConfigurableJoint j = axle.suspensionJoint;
            j.connectedBody = _chassis;
            j.autoConfigureConnectedAnchor = false;
            j.anchor = Vector3.zero;
            j.connectedAnchor = axle.topLocalOnChassis;
            j.axis = Vector3.right;
            j.secondaryAxis = Vector3.up;
            j.xMotion = ConfigurableJointMotion.Locked;
            // Travel limits and spring force are solved explicitly in ApplySuspensionSpring.
            // Keeping the joint's Y axis free avoids fighting that custom spring with an offset limit.
            j.yMotion = ConfigurableJointMotion.Free;
            j.zMotion = ConfigurableJointMotion.Locked;
            j.angularXMotion = ConfigurableJointMotion.Locked;
            j.angularYMotion = ConfigurableJointMotion.Locked;
            j.angularZMotion = ConfigurableJointMotion.Locked;
            j.enableCollision = false;
            // Do not project a suspension chain whose anchors are intentionally separated on Y.
            j.projectionMode = JointProjectionMode.None;
            j.projectionDistance = 0.02f;
            j.enablePreprocessing = false;
            j.projectionAngle = 5f;
            j.linearLimit = new SoftJointLimit
            {
                limit = suspensionTravel,
                bounciness = 0f,
                contactDistance = 0.005f
            };
            j.yDrive = MakeDrive(0f, 0f, 0f);
            j.zDrive = MakeDrive(0f, 0f, 0f);
            j.targetPosition = Vector3.zero;
        }

        void ConfigureWheelJoint(AxleSetup axle)
        {
            ConfigurableJoint j = axle.wheelJoint;
            j.connectedBody = axle.bottomBody;
            j.autoConfigureConnectedAnchor = false;
            j.anchor = Vector3.zero;
            j.connectedAnchor = axle.wheelLocalOnBottom;
            j.axis = wheelSpinAxis.sqrMagnitude > 0.0001f ? wheelSpinAxis.normalized : Vector3.up;
            j.secondaryAxis = Vector3.forward;
            j.xMotion = ConfigurableJointMotion.Locked;
            j.yMotion = ConfigurableJointMotion.Locked;
            j.zMotion = ConfigurableJointMotion.Locked;
            j.angularXMotion = ConfigurableJointMotion.Free;
            j.angularYMotion = ConfigurableJointMotion.Locked;
            j.angularZMotion = ConfigurableJointMotion.Locked;
            j.enableCollision = false;
            j.projectionMode = JointProjectionMode.None;
            j.projectionDistance = 0.01f;
            j.enablePreprocessing = false;
            j.angularXDrive = MakeDrive(0f, 0f, 0f);
            j.targetAngularVelocity = Vector3.zero;
        }

        static JointDrive MakeDrive(float spring, float damper, float maxForce)
        {
            var drive = new JointDrive
            {
                positionSpring = spring,
                positionDamper = damper,
                maximumForce = maxForce
            };
#pragma warning disable 0618
            drive.mode = JointDriveMode.PositionAndVelocity;
#pragma warning restore 0618
            return drive;
        }

        static float MeasureRadius(SphereCollider sphere)
        {
            if (sphere == null)
            {
                return 0.34f;
            }

            Vector3 scale = sphere.transform.lossyScale;
            float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            return sphere.radius * maxScale;
        }

        void AlignVehicleToGround()
        {
            if (_axles == null || _chassis == null)
            {
                return;
            }

            float sumGroundY = 0f;
            int hits = 0;
            for (int i = 0; i < _axles.Length; i++)
            {
                if (SampleGround(_axles[i]))
                {
                    sumGroundY += _axles[i].groundY;
                    hits++;
                }
            }

            if (hits == 0)
            {
                return;
            }

            float avgGroundY = sumGroundY / hits;
            float sumChassisY = 0f;
            for (int i = 0; i < _axles.Length; i++)
            {
                AxleSetup axle = _axles[i];
                float wheelCenterY = axle.groundY + axle.radius;
                float bottomY = wheelCenterY - axle.wheelLocalOnBottom.y;
                float mountY = bottomY + axle.restSpringLength;
                float chassisY = mountY - axle.topLocalOnChassis.y;
                sumChassisY += chassisY;
            }

            Vector3 pos = _chassis.position;
            pos.y = sumChassisY / _axles.Length;
            _chassis.position = pos;
            _chassis.rotation = Quaternion.identity;
            _chassis.linearVelocity = Vector3.zero;
            _chassis.angularVelocity = Vector3.zero;

            for (int i = 0; i < _axles.Length; i++)
            {
                PlaceAxleOnGround(_axles[i]);
            }
        }

        bool SampleGround(AxleSetup axle)
        {
            Vector3 mountWorld = chassisPhysics.TransformPoint(axle.topLocalOnChassis);
            Vector3 origin = mountWorld + Vector3.up * 4f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 12f, groundMask, QueryTriggerInteraction.Ignore))
            {
                axle.groundY = hit.point.y;
                return true;
            }

            axle.groundY = 0f;
            return false;
        }

        void PlaceAxleOnGround(AxleSetup axle)
        {
            Vector3 mount = chassisPhysics.TransformPoint(axle.topLocalOnChassis);
            Vector3 down = Vector3.down;

            float wheelCenterY = axle.groundY + axle.radius;
            Vector3 wheelPos = axle.wheelCollider.position;
            wheelPos.y = wheelCenterY;
            axle.bottom.rotation = chassisPhysics.rotation;
            axle.bottom.position = wheelPos - axle.bottom.rotation * axle.wheelLocalOnBottom;

            Vector3 bottomPos = axle.bottom.position;
            axle.wheelCollider.position = wheelPos;
            axle.wheelCollider.rotation = axle.bottom.rotation * axle.wheelLocalRotOnBottom;

            if (axle.wheelBody != null)
            {
                axle.wheelBody.position = wheelPos;
                axle.wheelBody.rotation = axle.wheelCollider.rotation;
                axle.wheelBody.linearVelocity = Vector3.zero;
                axle.wheelBody.angularVelocity = Vector3.zero;
            }

            if (axle.bottomBody != null)
            {
                axle.bottomBody.position = bottomPos;
                axle.bottomBody.rotation = axle.bottom.rotation;
                axle.bottomBody.linearVelocity = Vector3.zero;
                axle.bottomBody.angularVelocity = Vector3.zero;
            }

            float currentLength = Vector3.Dot(axle.bottom.position - mount, down);
            currentLength = Mathf.Clamp(currentLength, axle.minSpringLength, axle.maxSpringLength);
            Vector3 correctedBottom = mount + down * currentLength;
            axle.bottom.position = correctedBottom;
            if (axle.bottomBody != null)
            {
                axle.bottomBody.position = correctedBottom;
            }

            wheelPos = correctedBottom + axle.bottom.rotation * axle.wheelLocalOnBottom;
            axle.wheelCollider.position = wheelPos;
            if (axle.wheelBody != null)
            {
                axle.wheelBody.position = wheelPos;
            }
        }

        void FixedUpdate()
        {
            if (_physicsFrozen || _chassis == null || _axles == null)
            {
                return;
            }

            IsUpsideDown = Vector3.Dot(chassisPhysics.up, Vector3.up) < 0.15f;

            for (int i = 0; i < _axles.Length; i++)
            {
                ApplySuspensionSpring(_axles[i]);
            }

            if (Mathf.Abs(_throttle) < 0.02f)
            {
                HoldWhileIdle();
                return;
            }

            int grounded = 0;
            for (int i = 0; i < _axles.Length; i++)
            {
                AxleSetup axle = _axles[i];
                ApplyWheelMotor(axle);
                if (IsAxleGrounded(axle))
                {
                    grounded++;
                }
            }

            IsGrounded = grounded > 0;

            float pitch = IsGrounded ? wheelieAssist : airPitchTorque;
            _chassis.AddTorque(-chassisPhysics.right * (_throttle * pitch), ForceMode.Acceleration);
            if (!IsGrounded)
            {
                _chassis.angularVelocity *= Mathf.Clamp01(1f - airAngularDamping * Time.fixedDeltaTime);
            }

            LimitSpeed();
        }

        void ApplySuspensionSpring(AxleSetup axle)
        {
            if (axle.bottomBody == null)
            {
                return;
            }

            Vector3 mount = chassisPhysics.TransformPoint(axle.topLocalOnChassis);
            // The side-view vehicle uses world Y as the suspension axis.
            // Using chassis.up here creates a positive feedback loop once the body pitches.
            Vector3 down = Vector3.down;
            float currentLength = Vector3.Dot(axle.bottomBody.position - mount, down);
            currentLength = Mathf.Clamp(currentLength, axle.minSpringLength, axle.maxSpringLength);

            float error = axle.restSpringLength - currentLength;
            float relVel = Vector3.Dot(axle.bottomBody.linearVelocity - _chassis.linearVelocity, down);

            // Keep the explicit spring numerically stable at the project's fixed timestep.
            // The old damper value was far above critical damping for an 8 kg Bottom body,
            // which made the solver inject energy and launch the whole vehicle at idle.
            float effectiveMass = (bottomMass * mass) / Mathf.Max(0.01f, bottomMass + mass);
            float criticalDamper = 2f * Mathf.Sqrt(Mathf.Max(0.01f, suspensionSpring) * effectiveMass);
            float stableDamper = Mathf.Min(suspensionDamper, criticalDamper * 1.1f);
            float force = error * suspensionSpring - relVel * stableDamper;
            force = Mathf.Clamp(force, -maxSuspensionForce, maxSuspensionForce);

            axle.bottomBody.AddForce(down * force, ForceMode.Force);
            // Apply the reaction at the axle mount, not at the center of mass, so that a loaded
            // front or rear spring produces real pitch torque on the body.
            _chassis.AddForceAtPosition(-down * force, mount, ForceMode.Force);
        }

        void ApplyWheelMotor(AxleSetup axle)
        {
            if (axle.wheelBody == null)
            {
                return;
            }

            Vector3 axleAxis = GetSpinAxisWorld(axle.wheelCollider);
            float targetOmega = _throttle * maxWheelSpin;
            Vector3 ang = axle.wheelBody.angularVelocity;
            ang -= Vector3.Project(ang, axleAxis);
            ang += axleAxis * targetOmega;
            axle.wheelBody.angularVelocity = ang;
        }

        Vector3 GetSpinAxisWorld(Transform wheelRoot)
        {
            Vector3 local = wheelSpinAxis.sqrMagnitude > 0.0001f ? wheelSpinAxis.normalized : Vector3.up;
            return wheelRoot.TransformDirection(local);
        }

        void LimitSpeed()
        {
            float along = Vector3.Dot(_chassis.linearVelocity, chassisPhysics.forward);
            if (Mathf.Abs(along) <= maxSpeed)
            {
                return;
            }

            Vector3 vel = _chassis.linearVelocity;
            vel -= chassisPhysics.forward * along;
            vel += chassisPhysics.forward * (Mathf.Sign(along) * maxSpeed);
            _chassis.linearVelocity = vel;
        }

        void HoldWhileIdle()
        {
            int grounded = 0;
            for (int i = 0; i < _axles.Length; i++)
            {
                ApplyWheelMotor(_axles[i]);
                if (IsAxleGrounded(_axles[i]))
                {
                    grounded++;
                }

                DampSmallMotion(_axles[i].bottomBody);
                DampSmallMotion(_axles[i].wheelBody);
            }

            IsGrounded = grounded > 0;
            DampSmallMotion(_chassis);
            _chassis.angularVelocity *= 0.75f;
        }

        static void DampSmallMotion(Rigidbody rb)
        {
            if (rb == null)
            {
                return;
            }

            Vector3 v = rb.linearVelocity;
            v.x = 0f;
            v.z *= 0.65f;
            v.y *= 0.65f;
            rb.linearVelocity = v;
        }

        bool IsAxleGrounded(AxleSetup axle)
        {
            if (axle.wheelBody == null)
            {
                return false;
            }

            float radius = axle.radius > 0.05f ? axle.radius : wheelRadius;
            Vector3 origin = axle.wheelBody.position + Vector3.up * 0.05f;
            return Physics.Raycast(origin, Vector3.down, radius + 0.08f, groundMask, QueryTriggerInteraction.Ignore);
        }

        void ApplyKinematic(bool kinematic)
        {
            SetKinematic(_chassis, kinematic);
            if (_axles == null)
            {
                return;
            }

            for (int i = 0; i < _axles.Length; i++)
            {
                SetKinematic(_axles[i].bottomBody, kinematic);
                SetKinematic(_axles[i].wheelBody, kinematic);
            }
        }

        static void SetKinematic(Rigidbody rb, bool kinematic)
        {
            if (rb == null)
            {
                return;
            }

            rb.isKinematic = kinematic;
            if (kinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            else
            {
                rb.WakeUp();
            }
        }
    }
}
