using UnityEngine;
using UnityEngine.InputSystem;

namespace DriveMad
{
    public class DriveInput : MonoBehaviour
    {
        [SerializeField] DriveMadCarController car;
        [SerializeField] LevelSession session;

        float _uiForward;
        float _uiReverse;

        public void SetCar(DriveMadCarController value) => car = value;
        public void SetSession(LevelSession value) => session = value;

        public void SetForwardHeld(bool held) => _uiForward = held ? 1f : 0f;
        public void SetReverseHeld(bool held) => _uiReverse = held ? 1f : 0f;

        public void RequestRestart()
        {
            if (session != null)
            {
                session.Restart();
            }
        }

        void Update()
        {
            if (car == null)
            {
                return;
            }

            float throttle = _uiForward - _uiReverse;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                bool forward = keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed;
                bool reverse = keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed;
                float keyboardThrottle = (forward ? 1f : 0f) - (reverse ? 1f : 0f);
                if (forward || reverse)
                {
                    throttle = keyboardThrottle;
                }
                else
                {
                    throttle = Mathf.Clamp(_uiForward - _uiReverse, -1f, 1f);
                }

                if (keyboard.rKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame)
                {
                    RequestRestart();
                }
            }

            car.SetThrottle(Mathf.Clamp(throttle, -1f, 1f));
        }
    }
}
