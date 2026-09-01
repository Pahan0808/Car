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
            [System.NonSerialized] public float authoredSpringLength;
            [System.NonSerialized] public float restSpringLength;
            [System.NonSerialized] public float maxSpringLength;
            [System.NonSerialized] public float groundY;
        }

        [Header("Data")]
        [Tooltip("All tuning values live here. Scene wiring stays on this component.")]
        [SerializeField] CarSettings settings;

        [Header("Scene wiring")]
        [SerializeField] Transform chassisPhysics;
        [SerializeField] AxleSetup front = new AxleSetup { name = "Front" };
        [SerializeField] AxleSetup rear = new AxleSetup { name = "Rear" };

        const int WheelPhysicsLayer = 0;

        // Tuning is read through the settings asset. The property names match the previous fields so
        // the physics code below is untouched by the move to ScriptableObjects.
        float mass => Settings.mass;
        float centerOfMassHeight => Settings.centerOfMassHeight;
        float linearDamping => Settings.linearDamping;
        float angularDamping => Settings.angularDamping;
        float bottomMass => Settings.bottomMass;
        float wheelMass => Settings.wheelMass;
        bool autoConfigureJoints => Settings.autoConfigureJoints;
        float suspensionTravel => Settings.suspensionTravel;
        float bodyWheelClearance => Settings.bodyWheelClearance;
        float suspensionSpring => Settings.suspensionSpring;
        float suspensionDamper => Settings.suspensionDamper;
        float suspensionDampingRatio => Settings.suspensionDampingRatio;
        float maxSuspensionForce => Settings.maxSuspensionForce;
        float suspensionPitchTransfer => Settings.suspensionPitchTransfer;
        float maxWheelSpin => Settings.maxWheelSpin;
        float longitudinalGrip => Settings.longitudinalGrip;
        float maxTractionAccel => Settings.maxTractionAccel;
        float maxSpeed => Settings.maxSpeed;
        float wheelieAssist => Settings.wheelieAssist;
        float airPitchTorque => Settings.airPitchTorque;
        float airAngularDamping => Settings.airAngularDamping;
        Vector3 wheelSpinAxis => Settings.wheelSpinAxis;
        float wheelFriction => Settings.wheelFriction;
        float rollingResistance => Settings.rollingResistance;
        float upsideDownAngle => Settings.upsideDownAngle;
        LayerMask groundMask => Settings.groundMask;

        CarSettings _runtimeSettings;
        Rigidbody _chassis;
        AxleSetup[] _axles;
        PhysicsMaterial _wheelMaterial;
        Collider[] _chassisColliders;
        float _bodyBottomLocalY;
        float _throttle;
        bool _physicsFrozen;
        bool _wrecked;

        public float Throttle => _throttle;
        public bool IsGrounded { get; private set; }
        public bool IsUpsideDown { get; private set; }
        public Rigidbody Body => _chassis;
        public Transform Chassis => chassisPhysics != null ? chassisPhysics : transform;

        CarSettings Settings
        {
            get
            {
                if (settings != null)
                {
                    return settings;
                }

                if (_runtimeSettings == null)
                {
                    Debug.LogError($"DriveMad: {name} has no CarSettings assigned, falling back to defaults.", this);
                    _runtimeSettings = ScriptableObject.CreateInstance<CarSettings>();
                }

                return _runtimeSettings;
            }
        }

        public CarSettings CurrentSettings => Settings;
        public void SetSettings(CarSettings value) => settings = value;

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

        /// <summary>
        /// Crash state: stop driving the car and let the parts behave as plain rigid bodies.
        /// The wheels detach from the axles; the body already collides with the ground, so it only
        /// needs its wheel contacts back.
        /// </summary>
        public void Wreck()
        {
            if (_wrecked || _chassis == null || _axles == null)
            {
                return;
            }

            _wrecked = true;
            _throttle = 0f;

            for (int i = 0; i < _axles.Length; i++)
            {
                AxleSetup axle = _axles[i];

                // Wheels come off.
                if (axle.wheelJoint != null)
                {
                    Destroy(axle.wheelJoint);
                    axle.wheelJoint = null;
                }

                // The suspension stops solving, so lock it instead of letting Bottom slide forever
                // along the free Y axis: body + springs become one piece of debris.
                if (axle.suspensionJoint != null)
                {
                    axle.suspensionJoint.yMotion = ConfigurableJointMotion.Locked;
                }

                if (axle.bottomBody != null)
                {
                    axle.bottomBody.constraints = RigidbodyConstraints.FreezePositionX;
                }

                if (axle.wheelBody != null)
                {
                    axle.wheelBody.constraints = RigidbodyConstraints.FreezePositionX;
                }

                RestoreChassisCollision(axle.wheelCol);
            }

            _chassis.constraints = RigidbodyConstraints.FreezePositionX;
        }

        void RestoreChassisCollision(Collider wheelCol)
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
                    Physics.IgnoreCollision(wheelCol, c, false);
                }
            }
        }

        void Awake()
        {
            ResolveChassis();
            _axles = new[] { BindAxle(front), BindAxle(rear) };
        }

        void Start()
        {
            // The scene pose is the authored rest pose. Do not average axle heights or move the
            // chassis here: that would make front and rear springs start with different lengths.
            // Gravity is set by LevelSession.Awake, so the preload is computed here, not in Awake.
            CalculateCenterOfMass();
            ApplyStaticPreload();
            ResetRuntimeVelocities();
            Physics.SyncTransforms();
        }

        void ResetRuntimeVelocities()
        {
            if (_chassis != null)
            {
                _chassis.linearVelocity = Vector3.zero;
                _chassis.angularVelocity = Vector3.zero;
            }

            if (_axles == null)
            {
                return;
            }

            for (int i = 0; i < _axles.Length; i++)
            {
                if (_axles[i].bottomBody != null)
                {
                    _axles[i].bottomBody.linearVelocity = Vector3.zero;
                    _axles[i].bottomBody.angularVelocity = Vector3.zero;
                }

                if (_axles[i].wheelBody != null)
                {
                    _axles[i].wheelBody.linearVelocity = Vector3.zero;
                    _axles[i].wheelBody.angularVelocity = Vector3.zero;
                }
            }
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
                Debug.LogError($"DriveMad: SphereCollider radius is too small for {axle.name}.", this);
            }

            sphere.center = Vector3.zero;
            sphere.sharedMaterial = GetWheelMaterial();
        }

        /// <summary>
        /// Low PhysX friction so the wheels can roll and slide freely. Drive force comes from the
        /// explicit slip model in ApplyTraction, not from the contact solver.
        /// </summary>
        PhysicsMaterial GetWheelMaterial()
        {
            if (_wheelMaterial == null)
            {
                _wheelMaterial = new PhysicsMaterial("DriveMadWheel")
                {
                    frictionCombine = PhysicsMaterialCombine.Multiply,
                    bounceCombine = PhysicsMaterialCombine.Minimum,
                    bounciness = 0f
                };
            }

            _wheelMaterial.dynamicFriction = wheelFriction;
            _wheelMaterial.staticFriction = wheelFriction;
            return _wheelMaterial;
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

            // Fully compressed: body box bottom sits bodyWheelClearance above wheel tops.
            float chassisYAtFullCompress = axle.radius * 2f + bodyWheelClearance - _bodyBottomLocalY;
            float bottomYAtFullCompress = axle.radius - wheelCenterAboveBottom;
            float clearanceMin = Mathf.Max(0.02f, (mountLocalY + chassisYAtFullCompress) - bottomYAtFullCompress);

            // The ride height authored in the scene is the pose the car must keep while standing.
            Vector3 mount = chassisPhysics.TransformPoint(axle.topLocalOnChassis);
            float authored = Vector3.Dot(axle.bottom.position - mount, -chassisPhysics.up);
            if (authored <= 0.02f)
            {
                authored = clearanceMin + suspensionTravel;
            }

            axle.authoredSpringLength = authored;
            // Rest length gets its static preload in ApplyStaticPreload once both axles are known.
            axle.restSpringLength = authored;
            // Travel is symmetric around the authored height: the body can rise or drop while driving.
            axle.minSpringLength = Mathf.Max(Mathf.Min(clearanceMin, authored), authored - suspensionTravel);
            axle.maxSpringLength = authored + suspensionTravel;
        }

        /// <summary>
        /// Calculates the center of mass from the chassis geometry and axle positions.
        /// The longitudinal position is centered between the axle mounts so static load is balanced.
        /// </summary>
        /// <summary>
        /// Calculates the center of mass. X and Z are derived from chassis geometry and axle
        /// positions (Z centered between axle mounts so static load is balanced); Y (height) is an
        /// authored setting, since it directly controls wheelie / rollover sensitivity and should not
        /// silently follow the collider's bounding box.
        /// </summary>
        void CalculateCenterOfMass()
        {
            if (_chassis == null)
            {
                return;
            }

            Vector3 calculated = GetChassisGeometryCenter();
            calculated.y = centerOfMassHeight;
            if (_axles != null && _axles.Length >= 2)
            {
                calculated.z = (_axles[0].topLocalOnChassis.z + _axles[1].topLocalOnChassis.z) * 0.5f;
            }

            _chassis.centerOfMass = calculated;
        }

        Vector3 GetChassisGeometryCenter()
        {
            BoxCollider box = chassisPhysics != null ? chassisPhysics.GetComponent<BoxCollider>() : null;
            if (box != null)
            {
                return box.center;
            }

            if (_chassisColliders == null || _chassisColliders.Length == 0)
            {
                return Vector3.zero;
            }

            Vector3 weightedCenter = Vector3.zero;
            float totalWeight = 0f;
            for (int i = 0; i < _chassisColliders.Length; i++)
            {
                Collider collider = _chassisColliders[i];
                if (collider == null || collider.isTrigger)
                {
                    continue;
                }

                Vector3 localCenter = chassisPhysics.InverseTransformPoint(collider.bounds.center);
                Vector3 size = collider.bounds.size;
                float weight = Mathf.Max(0.0001f, size.x * size.y * size.z);
                weightedCenter += localCenter * weight;
                totalWeight += weight;
            }

            return totalWeight > 0f ? weightedCenter / totalWeight : Vector3.zero;
        }

        /// <summary>
        /// Static preload. At the authored length the spring error is zero, so it carries no weight and
        /// the body has to sink until compression matches the load. Offsetting the rest length by the
        /// per-axle static sag makes the authored pose the real equilibrium.
        /// </summary>
        void ApplyStaticPreload()
        {
            if (_chassis == null || _axles == null || _axles.Length == 0 || suspensionSpring <= 0.01f)
            {
                return;
            }

            float totalLoad = mass * Mathf.Abs(Physics.gravity.y);

            for (int i = 0; i < _axles.Length; i++)
            {
                float share = GetStaticLoadShare(i);
                float sag = (totalLoad * share) / suspensionSpring;
                _axles[i].restSpringLength = _axles[i].authoredSpringLength + sag;
            }
        }

        float GetStaticLoadShare(int index)
        {
            if (_axles.Length != 2)
            {
                return 1f / _axles.Length;
            }

            int other = index == 0 ? 1 : 0;
            float own = _axles[index].topLocalOnChassis.z;
            float opposite = _axles[other].topLocalOnChassis.z;
            float span = own - opposite;
            if (Mathf.Abs(span) < 0.001f)
            {
                return 0.5f;
            }

            // Lever rule around the opposite axle mount.
            return Mathf.Clamp01((_chassis.centerOfMass.z - opposite) / span);
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



        static float GetMaxScale(Vector3 scale)
        {
            return Mathf.Max(0.0001f, Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z))));
        }

        static float MeasureRadius(SphereCollider sphere)
        {
            if (sphere == null)
            {
                return 0f;
            }

            return sphere.radius * GetMaxScale(sphere.transform.lossyScale);
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
            if (_physicsFrozen || _wrecked || _chassis == null || _axles == null)
            {
                return;
            }

            IsUpsideDown = Vector3.Angle(chassisPhysics.up, Vector3.up) >= upsideDownAngle;

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
                if (TryGetGroundHit(axle, out RaycastHit hit))
                {
                    // Both axles are driven: traction is applied at each contact patch, so a hard
                    // launch naturally pitches the body up and can flip it over.
                    ApplyTraction(axle, hit);
                    grounded++;
                }
            }

            IsGrounded = grounded > 0;

            if (!IsGrounded)
            {
                _chassis.AddTorque(-chassisPhysics.right * (_throttle * airPitchTorque), ForceMode.Acceleration);
                _chassis.angularVelocity *= Mathf.Clamp01(1f - airAngularDamping * Time.fixedDeltaTime);
            }
            else if (wheelieAssist > 0f)
            {
                _chassis.AddTorque(-chassisPhysics.right * (_throttle * wheelieAssist), ForceMode.Acceleration);
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
            // Must match the joint's free axis, which is the chassis local Y. Measuring along world Y
            // instead makes the travel limit blind once the body pitches up, and the suspension
            // stretches without bound during a wheelie.
            Vector3 down = -chassisPhysics.up;
            float rawLength = Vector3.Dot(axle.bottomBody.position - mount, down);
            float currentLength = Mathf.Clamp(rawLength, axle.minSpringLength, axle.maxSpringLength);

            float error = axle.restSpringLength - currentLength;
            float force = Mathf.Clamp(error * suspensionSpring, -maxSuspensionForce, maxSuspensionForce);

            axle.bottomBody.AddForce(down * force, ForceMode.Force);
            ApplyToChassis(-down * force, mount, ForceMode.Force);

            ApplySuspensionDamping(axle, mount, down);
            ClampSuspensionTravel(axle, mount, down, rawLength);
        }

        /// <summary>
        /// Implicit (velocity level) damper. An explicit force damper explodes once the coefficient
        /// passes 2 * m / dt, which for a light Bottom body capped the damping far below what the
        /// suspension needs. Solving it as an impulse stays stable at any damping ratio.
        /// </summary>
        void ApplySuspensionDamping(AxleSetup axle, Vector3 mount, Vector3 down)
        {
            float relVel = Vector3.Dot(axle.bottomBody.linearVelocity - _chassis.linearVelocity, down);
            if (Mathf.Abs(relVel) < 0.0001f)
            {
                return;
            }

            float effectiveMass = (bottomMass * mass) / Mathf.Max(0.01f, bottomMass + mass);
            float criticalDamper = 2f * Mathf.Sqrt(Mathf.Max(0.01f, suspensionSpring) * effectiveMass);
            float damper = Mathf.Min(criticalDamper * Mathf.Max(0f, suspensionDampingRatio), suspensionDamper);

            float dt = Time.fixedDeltaTime;
            float blend = (damper * dt) / (effectiveMass + damper * dt);
            Vector3 impulse = down * (relVel * blend * effectiveMass);

            axle.bottomBody.AddForce(-impulse, ForceMode.Impulse);
            ApplyToChassis(impulse, mount, ForceMode.Impulse);
        }

        /// <summary>
        /// Part of the load goes to the axle mount so the body can pitch on slopes, the rest goes to
        /// the center of mass. Sending all of it to the mount makes the body rock at idle.
        /// </summary>
        void ApplyToChassis(Vector3 value, Vector3 mount, ForceMode mode)
        {
            _chassis.AddForceAtPosition(value * suspensionPitchTransfer, mount, mode);
            _chassis.AddForce(value * (1f - suspensionPitchTransfer), mode);
        }

        /// <summary>
        /// Hard bump stops. The joint keeps Y free so the explicit spring can solve, which means
        /// nothing else bounds the travel: without this the spring saturates and Bottom can drift
        /// past its range, even above the Top mount.
        /// </summary>
        void ClampSuspensionTravel(AxleSetup axle, Vector3 mount, Vector3 down, float rawLength)
        {
            float clamped = Mathf.Clamp(rawLength, axle.minSpringLength, axle.maxSpringLength);
            Vector3 offset = axle.bottomBody.position - mount;
            Vector3 lateral = offset - down * rawLength;
            bool outOfRange = Mathf.Abs(clamped - rawLength) > 0.0001f;

            // Snapping back onto the axis line also removes any sideways drift the solver allowed,
            // so the suspension can never look stretched.
            if (!outOfRange && lateral.sqrMagnitude < 0.0001f)
            {
                return;
            }

            axle.bottomBody.position = mount + down * clamped;

            if (!outOfRange)
            {
                return;
            }

            // Absorb the motion into the stop instead of bouncing off it.
            Vector3 v = axle.bottomBody.linearVelocity;
            float relAlong = Vector3.Dot(v - _chassis.linearVelocity, down);
            bool pushingIntoStop = rawLength > clamped ? relAlong > 0f : relAlong < 0f;
            if (pushingIntoStop)
            {
                axle.bottomBody.linearVelocity = v - down * relAlong;
            }
        }

        void ApplyWheelMotor(AxleSetup axle)
        {
            if (axle.wheelBody == null)
            {
                return;
            }

            Vector3 axleAxis = GetDriveAxisWorld(axle.wheelCollider);
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

        /// <summary>
        /// Spin axis oriented so that a positive angular velocity always rolls the car forward,
        /// regardless of how the wheel rig is rotated in the scene.
        /// </summary>
        Vector3 GetDriveAxisWorld(Transform wheelRoot)
        {
            Vector3 axis = GetSpinAxisWorld(wheelRoot);
            return Vector3.Dot(axis, chassisPhysics.right) < 0f ? -axis : axis;
        }

        void ApplyTraction(AxleSetup axle, RaycastHit hit)
        {
            Vector3 axis = GetDriveAxisWorld(axle.wheelCollider);
            float omega = Vector3.Dot(axle.wheelBody.angularVelocity, axis);

            Vector3 forward = Vector3.ProjectOnPlane(chassisPhysics.forward, hit.normal);
            if (forward.sqrMagnitude < 0.0001f)
            {
                return;
            }

            forward.Normalize();

            float radius = axle.radius;
            float wheelSurfaceSpeed = omega * radius;
            float bodySpeed = Vector3.Dot(_chassis.linearVelocity, forward);
            float slip = wheelSurfaceSpeed - bodySpeed;

            float accel = Mathf.Clamp(slip * longitudinalGrip, -maxTractionAccel, maxTractionAccel);
            float loadPerAxle = mass / Mathf.Max(1, _axles.Length);

            // Force enters the body at ground level, so the moment arm to the center of mass
            // produces the wheelie / roll-over behaviour instead of a pure translation.
            _chassis.AddForceAtPosition(forward * (accel * loadPerAxle), hit.point, ForceMode.Force);
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
            Vector3 normalSum = Vector3.zero;
            for (int i = 0; i < _axles.Length; i++)
            {
                // No hard brake with the throttle released: the wheels keep rolling, they only
                // lose spin through rolling resistance so the car can still coast downhill.
                if (TryGetGroundHit(_axles[i], out RaycastHit hit))
                {
                    grounded++;
                    normalSum += hit.normal;
                }

                ApplyRollingResistance(_axles[i]);
            }

            IsGrounded = grounded > 0;

            // No parking assist: the car coasts and stops purely through physics.
            for (int i = 0; i < _axles.Length; i++)
            {
                DampSmallMotion(_axles[i].bottomBody);
                DampSmallMotion(_axles[i].wheelBody);
            }

            DampSmallMotion(_chassis);

            // Calm the body pitch while coasting so the springs settle instead of rocking.
            _chassis.angularVelocity *= Mathf.Clamp01(1f - 2f * Time.fixedDeltaTime);
        }

        void ApplyRollingResistance(AxleSetup axle)
        {
            if (axle.wheelBody == null)
            {
                return;
            }

            axle.wheelBody.angularVelocity *= Mathf.Clamp01(1f - rollingResistance * Time.fixedDeltaTime);
        }

        /// <summary>
        /// Keeps the side-view rig on its lateral rail. No longitudinal damping: braking and
        /// stopping are left to physics.
        /// </summary>
        static void DampSmallMotion(Rigidbody rb)
        {
            if (rb == null)
            {
                return;
            }

            Vector3 v = rb.linearVelocity;
            v.x = 0f;
            rb.linearVelocity = v;
        }

        bool IsAxleGrounded(AxleSetup axle) => TryGetGroundHit(axle, out _);

        bool TryGetGroundHit(AxleSetup axle, out RaycastHit hit)
        {
            hit = default;
            if (axle.wheelBody == null)
            {
                return false;
            }

            float radius = axle.radius;
            Vector3 origin = axle.wheelBody.position + Vector3.up * 0.05f;
            return Physics.Raycast(origin, Vector3.down, out hit, radius + 0.08f, groundMask,
                QueryTriggerInteraction.Ignore);
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
