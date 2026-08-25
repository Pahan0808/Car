using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DriveMad
{
    public class LevelSession : MonoBehaviour
    {
        public enum Outcome
        {
            Playing,
            Won,
            Crashed,
            Fell
        }

        [Header("Data")]
        [SerializeField] GameSettings settings;

        [Header("Scene wiring")]
        [SerializeField] DriveMadCarController car;
        [SerializeField] Text statusText;

        GameSettings _runtimeSettings;
        Outcome _outcome = Outcome.Playing;
        float _upsideDownTimer;

        public Outcome Current => _outcome;
        public bool IsPlaying => _outcome == Outcome.Playing;

        public void SetCar(DriveMadCarController value) => car = value;
        public void SetStatusText(Text value) => statusText = value;
        public void SetSettings(GameSettings value) => settings = value;

        GameSettings Settings
        {
            get
            {
                if (settings != null)
                {
                    return settings;
                }

                if (_runtimeSettings == null)
                {
                    Debug.LogError($"DriveMad: {name} has no GameSettings assigned, falling back to defaults.", this);
                    _runtimeSettings = ScriptableObject.CreateInstance<GameSettings>();
                }

                return _runtimeSettings;
            }
        }

        void Awake()
        {
            GameSettings s = Settings;
            Physics.gravity = s.gravity;
            // The body must collide with the ground at all times, not only after a crash.
            // Wheel-vs-body contacts are disabled per collider pair in the car controller instead.
            Physics.IgnoreLayerCollision(s.groundLayer, s.vehicleLayer, false);
            ShowStatus(string.Empty);
        }

        void Update()
        {
            if (!IsPlaying || car == null)
            {
                return;
            }

            Vector3 carPos = car.Body != null ? car.Body.position : car.Chassis.position;
            if (carPos.y < Settings.fallY)
            {
                NotifyFell();
                return;
            }

            UpdateRollOver();
        }

        void UpdateRollOver()
        {
            if (!car.IsUpsideDown)
            {
                _upsideDownTimer = 0f;
                return;
            }

            _upsideDownTimer += Time.deltaTime;
            if (_upsideDownTimer >= Settings.upsideDownFailTime)
            {
                Fail(Outcome.Crashed, Settings.crashMessage);
            }
        }

        public void NotifyFell()
        {
            Fail(Outcome.Fell, Settings.fellMessage);
        }

        public void Win()
        {
            if (!IsPlaying)
            {
                return;
            }

            _outcome = Outcome.Won;
            car.SetThrottle(0f);
            car.FreezePhysics(true);
            ShowStatus(Settings.winMessage);
        }

        public void Restart()
        {
            Scene scene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(scene.buildIndex);
        }

        void Fail(Outcome outcome, string message)
        {
            if (!IsPlaying)
            {
                return;
            }

            _outcome = outcome;
            car.SetThrottle(0f);
            // Do not freeze on a crash: the car falls apart and keeps colliding as debris.
            car.Wreck();
            ShowStatus(message);
        }

        void ShowStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
        }
    }
}
