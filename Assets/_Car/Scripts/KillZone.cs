using UnityEngine;

namespace DriveMad
{
    public class KillZone : MonoBehaviour
    {
        [SerializeField] LevelSession session;

        public void SetSession(LevelSession value) => session = value;

        void OnTriggerEnter(Collider other)
        {
            if (session == null || session.Current != LevelSession.Outcome.Playing)
            {
                return;
            }

            if (other.GetComponentInParent<DriveMadCarController>() != null)
            {
                session.NotifyFell();
            }
        }
    }
}
