using UnityEngine;

namespace DriveMad
{
    public class SideViewFollowCamera : MonoBehaviour
    {
        [SerializeField] Transform target;
        [SerializeField] Vector3 offset = new Vector3(11.5f, 4.2f, 2.8f);
        [SerializeField] float followDamping = 6.5f;
        [SerializeField] float lookAhead = 3.2f;

        Vector3 _velocity;

        public void SetTarget(Transform value) => target = value;

        void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            Vector3 desired = target.position + offset;
            desired.z += lookAhead;
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref _velocity, 1f / followDamping);
            transform.rotation = Quaternion.LookRotation(target.position + Vector3.up * 0.6f - transform.position, Vector3.up);
        }
    }
}
