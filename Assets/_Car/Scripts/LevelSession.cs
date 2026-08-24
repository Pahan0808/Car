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

        [SerializeField] DriveMadCarController car;
        [SerializeField] Text statusText;
        [SerializeField] float upsideDownFailTime = 0.55f;
        [SerializeField] float fallY = -12f;
        [SerializeField] Vector3 gravity = new Vector3(0f, -12.5f, 0f);

        Outcome _outcome = Outcome.Playing;
        float _upsideDownTimer;

        public Outcome Current => _outcome;

        public void SetCar(DriveMadCarController value) => car = value;
        public void SetStatusText(Text value) => statusText = value;

        void Awake()
        {
            Physics.gravity = gravity;
            Physics.IgnoreLayerCollision(8, 9, true);
            ShowStatus(string.Empty);
        }

        void Update()
        {
            if (_outcome != Outcome.Playing || car == null)
            {
                return;
            }

            Vector3 carPos = car.Body != null ? car.Body.position : car.Chassis.position;
            if (carPos.y < fallY)
            {
                NotifyFell();
                return;
            }

            if (car.IsUpsideDown)
            {
                _upsideDownTimer += Time.deltaTime;
                if (_upsideDownTimer >= upsideDownFailTime)
                {
                    Fail(Outcome.Crashed, "CRASH — R / Space");
                }
            }
            else
            {
                _upsideDownTimer = 0f;
            }
        }

        public void NotifyFell()
        {
            Fail(Outcome.Fell, "FELL — R / Space");
        }

        public void Win()
        {
            if (_outcome != Outcome.Playing)
            {
                return;
            }

            _outcome = Outcome.Won;
            car.SetThrottle(0f);
            car.FreezePhysics(true);
            ShowStatus("FINISH");
        }

        public void Restart()
        {
            Scene scene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(scene.buildIndex);
        }

        void Fail(Outcome outcome, string message)
        {
            if (_outcome != Outcome.Playing)
            {
                return;
            }

            _outcome = outcome;
            car.SetThrottle(0f);
            car.FreezePhysics(true);
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
