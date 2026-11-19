using UnityEngine;
using UnityEngine.UI;

namespace BaseConquest.Base
{
    [RequireComponent(typeof(Graphic))]
    public sealed class VillageTitleShade : BaseMeshEffect
    {
        public override void ModifyMesh(VertexHelper vertices)
        {
            if (!IsActive()) return;
            var rect = graphic.rectTransform.rect;
            var vertex = UIVertex.simpleVert;
            for (int i = 0; i < vertices.currentVertCount; i++)
            {
                vertices.PopulateUIVertex(ref vertex, i);
                float x = Mathf.InverseLerp(rect.xMin, rect.xMax, vertex.position.x);
                vertex.color.a = (byte)Mathf.RoundToInt(vertex.color.a * (1 - x));
                vertices.SetUIVertex(vertex, i);
            }
        }
    }
}
