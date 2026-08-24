using UnityEngine;
using UnityEngine.UI;

namespace DriveMad
{
    [RequireComponent(typeof(Button))]
    public class UiRestartButton : MonoBehaviour
    {
        [SerializeField] DriveInput input;

        public void SetInput(DriveInput value) => input = value;

        void Awake()
        {
            GetComponent<Button>().onClick.AddListener(RequestRestart);
        }

        void RequestRestart()
        {
            if (input != null)
            {
                input.RequestRestart();
            }
        }
    }
}
