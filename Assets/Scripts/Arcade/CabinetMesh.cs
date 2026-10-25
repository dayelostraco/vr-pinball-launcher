using System.Collections.Generic;
using UnityEngine;

namespace VRLauncher
{
    /// <summary>Meshes for cabinet parts that Unity's primitives cannot make.</summary>
    public static class CabinetMesh
    {
        /// <summary>
        /// A solid wedge <paramref name="width"/> wide, centred on X, whose side profile is the right
        /// triangle (zStart, yBottom), (zEnd, yTop), (zEnd, yBottom): a flat bottom, a vertical back
        /// and a rising slope. Faces do not share vertices, so each is flat-shaded, and every
        /// triangle is wound to face outward.
        /// </summary>
        public static Mesh Wedge(float width, float zStart, float zEnd, float yBottom, float yTop)
        {
            float half = width / 2f;
            var leftFront = new Vector3(-half, yBottom, zStart);
            var leftTop = new Vector3(-half, yTop, zEnd);
            var leftBack = new Vector3(-half, yBottom, zEnd);
            var rightFront = new Vector3(half, yBottom, zStart);
            var rightTop = new Vector3(half, yTop, zEnd);
            var rightBack = new Vector3(half, yBottom, zEnd);

            // Any point with positive weight on every corner is strictly inside the solid.
            Vector3 inside = (leftFront + leftTop + leftBack + rightFront + rightTop + rightBack) / 6f;

            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            // Corners go round the face's perimeter; winding is fixed per triangle below.
            void Face(params Vector3[] corners)
            {
                int start = vertices.Count;
                vertices.AddRange(corners);
                for (int i = 1; i < corners.Length - 1; i++)
                {
                    int a = start, b = start + i, c = start + i + 1;
                    Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                    Vector3 centroid = (vertices[a] + vertices[b] + vertices[c]) / 3f;
                    if (Vector3.Dot(normal, centroid - inside) < 0f)
                    {
                        (b, c) = (c, b);
                    }
                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(c);
                }
            }

            Face(leftFront, leftTop, leftBack);
            Face(rightFront, rightTop, rightBack);
            Face(leftFront, leftBack, rightBack, rightFront);   // bottom
            Face(leftBack, leftTop, rightTop, rightBack);       // back
            Face(leftFront, rightFront, rightTop, leftTop);     // slope

            var mesh = new Mesh { name = "CabinetWedge" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
