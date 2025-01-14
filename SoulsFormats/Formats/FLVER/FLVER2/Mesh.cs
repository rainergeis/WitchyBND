﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SoulsFormats;

public partial class FLVER2
{
    /// <summary>
    ///     An individual chunk of a model.
    /// </summary>
    public class Mesh : IFlverMesh
    {

        private int[] faceSetIndices, vertexBufferIndices;

        /// <summary>
        ///     Creates a new Mesh with default values.
        /// </summary>
        public Mesh()
        {
            DefaultBoneIndex = -1;
            BoneIndices = new List<int>();
            FaceSets = new List<FaceSet>();
            VertexBuffers = new List<VertexBuffer>();
            Vertices = new List<FLVER.Vertex>();
        }

        internal Mesh(BinaryReaderEx br, FLVERHeader header)
        {
            Dynamic = br.AssertByte(0, 1);
            br.AssertByte(0);
            br.AssertByte(0);
            br.AssertByte(0);

            MaterialIndex = br.ReadInt32();
            br.AssertInt32(0);
            br.AssertInt32(0);
            DefaultBoneIndex = br.ReadInt32();
            var boneCount = br.ReadInt32();
            var boundingBoxOffset = br.ReadInt32();
            var boneOffset = br.ReadInt32();
            var faceSetCount = br.ReadInt32();
            var faceSetOffset = br.ReadInt32();
            var vertexBufferCount = br.AssertInt32(1, 2, 3);
            var vertexBufferOffset = br.ReadInt32();

            if (boundingBoxOffset != 0)
            {
                br.StepIn(boundingBoxOffset);
                {
                    BoundingBox = new BoundingBoxes(br, header);
                }
                br.StepOut();
            }

            BoneIndices = new List<int>(br.GetInt32s(boneOffset, boneCount));
            faceSetIndices = br.GetInt32s(faceSetOffset, faceSetCount);
            vertexBufferIndices = br.GetInt32s(vertexBufferOffset, vertexBufferCount);
        }

        /// <summary>
        ///     Apparently does nothing. Usually points to a dummy bone named after the model, possibly just for labelling.
        /// </summary>
        public int DefaultBoneIndex { get; set; }

        /// <summary>
        ///     Indexes of bones in the bone collection which may be used by vertices in this mesh.
        /// </summary>
        public List<int> BoneIndices { get; set; }

        /// <summary>
        ///     Triangles in this mesh.
        /// </summary>
        public List<FaceSet> FaceSets { get; set; }

        /// <summary>
        ///     Vertex buffers in this mesh.
        /// </summary>
        public List<VertexBuffer> VertexBuffers { get; set; }

        /// <summary>
        ///     Vertices in this mesh.
        /// </summary>
        public List<FLVER.Vertex> Vertices { get; set; }

        /// <summary>
        ///     Optional bounding box struct; may be null.
        /// </summary>
        public BoundingBoxes BoundingBox { get; set; }

        /// <summary>
        ///     When 1, mesh is in bind pose; when 0, it isn't. Most likely has further implications.
        /// </summary>
        public byte Dynamic { get; set; }

        /// <summary>
        ///     Index of the material used by all triangles in this mesh.
        /// </summary>
        public int MaterialIndex { get; set; }

        IReadOnlyList<FLVER.Vertex> IFlverMesh.Vertices => Vertices;

        internal void TakeFaceSets(Dictionary<int, FaceSet> faceSetDict)
        {
            FaceSets = new List<FaceSet>(faceSetIndices.Length);
            foreach (var i in faceSetIndices)
            {
                if (!faceSetDict.ContainsKey(i))
                    throw new NotSupportedException("Face set not found or already taken: " + i);

                FaceSets.Add(faceSetDict[i]);
                faceSetDict.Remove(i);
            }

            faceSetIndices = null;
        }

        internal void TakeVertexBuffers(Dictionary<int, VertexBuffer> vertexBufferDict, List<BufferLayout> layouts)
        {
            VertexBuffers = new List<VertexBuffer>(vertexBufferIndices.Length);
            foreach (var i in vertexBufferIndices)
            {
                if (!vertexBufferDict.ContainsKey(i))
                    throw new NotSupportedException("Vertex buffer not found or already taken: " + i);

                VertexBuffers.Add(vertexBufferDict[i]);
                vertexBufferDict.Remove(i);
            }

            vertexBufferIndices = null;

            // Make sure no semantics repeat that aren't known to
            var semantics = new List<FLVER.LayoutSemantic>();
            foreach (VertexBuffer buffer in VertexBuffers)
            foreach (FLVER.LayoutMember member in layouts[buffer.LayoutIndex])
                if (member.Semantic != FLVER.LayoutSemantic.UV
                    && member.Semantic != FLVER.LayoutSemantic.Tangent
                    && member.Semantic != FLVER.LayoutSemantic.VertexColor
                    && member.Semantic != FLVER.LayoutSemantic.Position
                    && member.Semantic != FLVER.LayoutSemantic.Normal)
                {
                    if (semantics.Contains(member.Semantic))
                        throw new NotImplementedException("Unexpected semantic list.");
                    semantics.Add(member.Semantic);
                }

            for (var i = 0; i < VertexBuffers.Count; i++)
            {
                VertexBuffer buffer = VertexBuffers[i];
                // This appears to be some kind of flag on edge-compressed vertex buffers
                if ((buffer.BufferIndex & ~0x60000000) != i)
                    throw new FormatException("Unexpected vertex buffer index.");
            }
        }

        internal void ReadVertices(BinaryReaderEx br, int dataOffset, List<BufferLayout> layouts, FLVERHeader header)
        {
            IEnumerable<FLVER.LayoutMember> layoutMembers = layouts.SelectMany(l => l);
            var uvCap = layoutMembers.Where(m => m.Semantic == FLVER.LayoutSemantic.UV).Count();
            var tanCap = layoutMembers.Where(m => m.Semantic == FLVER.LayoutSemantic.Tangent).Count();
            var colorCap = layoutMembers.Where(m => m.Semantic == FLVER.LayoutSemantic.VertexColor).Count();

            var vertexCount = VertexBuffers[0].VertexCount;
            Vertices = new List<FLVER.Vertex>(vertexCount);
            for (var i = 0; i < vertexCount; i++)
                Vertices.Add(new FLVER.Vertex(uvCap, tanCap, colorCap));

            foreach (VertexBuffer buffer in VertexBuffers)
                buffer.ReadBuffer(br, layouts, Vertices, dataOffset, header);
        }

        internal void Write(BinaryWriterEx bw, int index)
        {
            bw.WriteByte(Dynamic);
            bw.WriteByte(0);
            bw.WriteByte(0);
            bw.WriteByte(0);

            bw.WriteInt32(MaterialIndex);
            bw.WriteInt32(0);
            bw.WriteInt32(0);
            bw.WriteInt32(DefaultBoneIndex);
            bw.WriteInt32(BoneIndices.Count);
            bw.ReserveInt32($"MeshBoundingBox{index}");
            bw.ReserveInt32($"MeshBoneIndices{index}");
            bw.WriteInt32(FaceSets.Count);
            bw.ReserveInt32($"MeshFaceSetIndices{index}");
            bw.WriteInt32(VertexBuffers.Count);
            bw.ReserveInt32($"MeshVertexBufferIndices{index}");
        }

        internal void WriteBoundingBox(BinaryWriterEx bw, int index, FLVERHeader header)
        {
            if (BoundingBox == null)
            {
                bw.FillInt32($"MeshBoundingBox{index}", 0);
            }
            else
            {
                bw.FillInt32($"MeshBoundingBox{index}", (int)bw.Position);
                BoundingBox.Write(bw, header);
            }
        }

        internal void WriteBoneIndices(BinaryWriterEx bw, int index, int boneIndicesStart)
        {
            if (BoneIndices.Count == 0)
            {
                // Just a weird case for byte-perfect writing
                bw.FillInt32($"MeshBoneIndices{index}", boneIndicesStart);
            }
            else
            {
                bw.FillInt32($"MeshBoneIndices{index}", (int)bw.Position);
                bw.WriteInt32s(BoneIndices);
            }
        }

        /// <summary>
        ///     Returns a list of arrays of 3 vertices, each representing a triangle in the mesh.
        ///     Faces are taken from the first FaceSet in the mesh with the given flags,
        ///     using None by default for the highest detail mesh. If not found, the first FaceSet is used.
        /// </summary>
        public List<FLVER.Vertex[]> GetFaces(FaceSet.FSFlags fsFlags = FaceSet.FSFlags.None)
        {
            if (FaceSets.Count == 0)
            {
                return new List<FLVER.Vertex[]>();
            }

            FaceSet faceSet = FaceSets.Find(fs => fs.Flags == fsFlags) ?? FaceSets[0];
            List<int> indices = faceSet.Triangulate(Vertices.Count < ushort.MaxValue);
            var vertices = new List<FLVER.Vertex[]>(indices.Count);
            for (var i = 0; i < indices.Count - 2; i += 3)
            {
                var vi1 = indices[i];
                var vi2 = indices[i + 1];
                var vi3 = indices[i + 2];
                vertices.Add(new[] { Vertices[vi1], Vertices[vi2], Vertices[vi3] });
            }

            return vertices;
        }

        /// <summary>
        ///     An optional bounding box for meshes added in DS2.
        /// </summary>
        public class BoundingBoxes
        {

            /// <summary>
            ///     Creates a BoundingBoxes with default values.
            /// </summary>
            public BoundingBoxes()
            {
                Min = new Vector3(float.MinValue);
                Max = new Vector3(float.MaxValue);
            }

            internal BoundingBoxes(BinaryReaderEx br, FLVERHeader header)
            {
                Min = br.ReadVector3();
                Max = br.ReadVector3();
                if (header.Version >= 0x2001A)
                    Unk = br.ReadVector3();
            }

            /// <summary>
            ///     Minimum extent of the mesh.
            /// </summary>
            public Vector3 Min { get; set; }

            /// <summary>
            ///     Maximum extent of the mesh.
            /// </summary>
            public Vector3 Max { get; set; }

            /// <summary>
            ///     Unknown; only present in Sekiro.
            /// </summary>
            public Vector3 Unk { get; set; }

            internal void Write(BinaryWriterEx bw, FLVERHeader header)
            {
                bw.WriteVector3(Min);
                bw.WriteVector3(Max);
                if (header.Version >= 0x2001A)
                    bw.WriteVector3(Unk);
            }
        }
    }
}