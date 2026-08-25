using UnityEngine;

namespace DriveMad
{
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public class SmoothHill : MonoBehaviour
    {
        [SerializeField] float length = 44f;
        [SerializeField] float width = 6.5f;
        [SerializeField] float height = 7.2f;
        [SerializeField] float startFlat = 10f;
        [SerializeField] float endFlat = 8f;
        [SerializeField] float thickness = 6f;
        [SerializeField] int segments = 48;

        public float Length => length;
        public float Height => height;
        public float Width => width;
        public float StartFlat => startFlat;

        void OnEnable()
        {
            Build();
        }

        public float SampleHeight(float localZ)
        {
            if (localZ <= startFlat)
            {
                return 0f;
            }

            float riseStart = startFlat;
            float riseEnd = length - endFlat;
            if (localZ >= riseEnd)
            {
                return height;
            }

            float t = Mathf.InverseLerp(riseStart, riseEnd, localZ);
            return height * t * t * (3f - 2f * t);
        }

        public void Build()
        {
            Mesh mesh = CreateMesh();
            mesh.name = "Level01_Hill";

            var filter = GetComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var collider = GetComponent<MeshCollider>();
            collider.sharedMesh = null;
            collider.sharedMesh = mesh;
            collider.convex = false;
        }

        Mesh CreateMesh()
        {
            int count = Mathf.Max(8, segments) + 1;
            var vertices = new Vector3[count * 4];
            var normals = new Vector3[count * 4];
            var uvs = new Vector2[count * 4];
            var triangles = new int[(count - 1) * 24];

            float halfWidth = width * 0.5f;
            int tri = 0;

            for (int i = 0; i < count; i++)
            {
                float z = length * i / (count - 1);
                float y = SampleHeight(z);
                float v = z / length;

                vertices[i] = new Vector3(-halfWidth, y, z);
                vertices[count + i] = new Vector3(halfWidth, y, z);
                vertices[count * 2 + i] = new Vector3(-halfWidth, y - thickness, z);
                vertices[count * 3 + i] = new Vector3(halfWidth, y - thickness, z);

                uvs[i] = new Vector2(0f, v);
                uvs[count + i] = new Vector2(1f, v);
                uvs[count * 2 + i] = new Vector2(0f, v);
                uvs[count * 3 + i] = new Vector2(1f, v);
            }

            void AddQuad(int a, int b, int c, int d)
            {
                triangles[tri++] = a;
                triangles[tri++] = b;
                triangles[tri++] = c;
                triangles[tri++] = b;
                triangles[tri++] = d;
                triangles[tri++] = c;
            }

            for (int i = 0; i < count - 1; i++)
            {
                int topL = i;
                int topR = count + i;
                int botL = count * 2 + i;
                int botR = count * 3 + i;

                AddQuad(topL, topL + 1, topR, topR + 1);
                AddQuad(botL + 1, botL, botR + 1, botR);
                AddQuad(topL, botL, topL + 1, botL + 1);
                AddQuad(topR + 1, botR + 1, topR, botR);
            }

            Mesh mesh = new Mesh();
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
