using UnityEngine;
using UnityEngine.EventSystems;

namespace DriveMad
{
    public class OnScreenHoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public enum ActionType
        {
            Forward,
            Reverse
        }

        [SerializeField] DriveInput input;
        [SerializeField] ActionType action;

        public void SetInput(DriveInput value) => input = value;
        public void SetAction(ActionType value) => action = value;

        public void OnPointerDown(PointerEventData eventData) => SetHeld(true);

        public void OnPointerUp(PointerEventData eventData) => SetHeld(false);

        public void OnPointerExit(PointerEventData eventData) => SetHeld(false);

        void OnDisable() => SetHeld(false);

        void SetHeld(bool held)
        {
            if (input == null)
            {
                return;
            }

            if (action == ActionType.Forward)
            {
                input.SetForwardHeld(held);
            }
            else
            {
                input.SetReverseHeld(held);
            }
        }
    }
}
