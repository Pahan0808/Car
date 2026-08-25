using UnityEngine;

namespace DriveMad
{
    public class FinishTrigger : MonoBehaviour
    {
        [SerializeField] LevelSession session;

        public void SetSession(LevelSession value) => session = value;

        void OnTriggerEnter(Collider other)
        {
            if (session == null)
            {
                return;
            }

            if (other.GetComponentInParent<DriveMadCarController>() != null)
            {
                session.Win();
            }
        }
    }
}
