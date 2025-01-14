﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SoulsFormats;

public partial class MSBS
{
    internal enum ModelType : uint
    {
        MapPiece = 0,
        Object = 1,
        Enemy = 2,
        Player = 4,
        Collision = 5
    }

    /// <summary>
    ///     Model files that are available for parts to use.
    /// </summary>
    public class ModelParam : Param<Model>, IMsbParam<IMsbModel>
    {

        /// <summary>
        ///     Creates an empty ModelParam with the default version.
        /// </summary>
        public ModelParam() : base(35, "MODEL_PARAM_ST")
        {
            MapPieces = new List<Model.MapPiece>();
            Objects = new List<Model.Object>();
            Enemies = new List<Model.Enemy>();
            Players = new List<Model.Player>();
            Collisions = new List<Model.Collision>();
        }

        /// <summary>
        ///     Models for fixed terrain and scenery.
        /// </summary>
        public List<Model.MapPiece> MapPieces { get; set; }

        /// <summary>
        ///     Models for dynamic props.
        /// </summary>
        public List<Model.Object> Objects { get; set; }

        /// <summary>
        ///     Models for non-player entities.
        /// </summary>
        public List<Model.Enemy> Enemies { get; set; }

        /// <summary>
        ///     Models for player spawn points, I think.
        /// </summary>
        public List<Model.Player> Players { get; set; }

        /// <summary>
        ///     Models for physics collision.
        /// </summary>
        public List<Model.Collision> Collisions { get; set; }

        IMsbModel IMsbParam<IMsbModel>.Add(IMsbModel item)
        {
            return Add((Model)item);
        }

        IReadOnlyList<IMsbModel> IMsbParam<IMsbModel>.GetEntries()
        {
            return GetEntries();
        }

        /// <summary>
        ///     Adds a model to the appropriate list for its type; returns the model.
        /// </summary>
        public Model Add(Model model)
        {
            switch (model)
            {
                case Model.MapPiece m:
                    MapPieces.Add(m);
                    break;
                case Model.Object m:
                    Objects.Add(m);
                    break;
                case Model.Enemy m:
                    Enemies.Add(m);
                    break;
                case Model.Player m:
                    Players.Add(m);
                    break;
                case Model.Collision m:
                    Collisions.Add(m);
                    break;

                default:
                    throw new ArgumentException($"Unrecognized type {model.GetType()}.", nameof(model));
            }

            return model;
        }

        /// <summary>
        ///     Returns every Model in the order they will be written.
        /// </summary>
        public override List<Model> GetEntries()
        {
            return SFUtil.ConcatAll<Model>(
                MapPieces, Objects, Enemies, Players, Collisions);
        }

        internal override Model ReadEntry(BinaryReaderEx br)
        {
            ModelType type = br.GetEnum32<ModelType>(br.Position + 8);
            switch (type)
            {
                case ModelType.MapPiece:
                    return MapPieces.EchoAdd(new Model.MapPiece(br));

                case ModelType.Object:
                    return Objects.EchoAdd(new Model.Object(br));

                case ModelType.Enemy:
                    return Enemies.EchoAdd(new Model.Enemy(br));

                case ModelType.Player:
                    return Players.EchoAdd(new Model.Player(br));

                case ModelType.Collision:
                    return Collisions.EchoAdd(new Model.Collision(br));

                default:
                    throw new NotImplementedException($"Unimplemented model type: {type}");
            }
        }
    }

    /// <summary>
    ///     A model file available for parts to reference.
    /// </summary>
    public abstract class Model : Entry, IMsbModel
    {

        private int InstanceCount;

        private protected Model(string name)
        {
            Name = name;
            SibPath = "";
        }

        private protected Model(BinaryReaderEx br)
        {
            var start = br.Position;
            var nameOffset = br.ReadInt64();
            br.AssertUInt32((uint)Type);
            br.ReadInt32(); // ID
            var sibOffset = br.ReadInt64();
            InstanceCount = br.ReadInt32();
            Unk1C = br.ReadInt32();
            var typeDataOffset = br.ReadInt64();

            if (nameOffset == 0)
                throw new InvalidDataException($"{nameof(nameOffset)} must not be 0 in type {GetType()}.");
            if (sibOffset == 0)
                throw new InvalidDataException($"{nameof(sibOffset)} must not be 0 in type {GetType()}.");
            if (HasTypeData ^ (typeDataOffset != 0))
                throw new InvalidDataException(
                    $"Unexpected {nameof(typeDataOffset)} 0x{typeDataOffset:X} in type {GetType()}.");

            br.Position = start + nameOffset;
            Name = br.ReadUTF16();

            br.Position = start + sibOffset;
            SibPath = br.ReadUTF16();

            if (HasTypeData)
            {
                br.Position = start + typeDataOffset;
                ReadTypeData(br);
            }
        }

        private protected abstract ModelType Type { get; }
        private protected abstract bool HasTypeData { get; }

        /// <summary>
        ///     A path to a .sib file, presumed to be some kind of editor placeholder.
        /// </summary>
        public string SibPath { get; set; }

        /// <summary>
        ///     Unknown.
        /// </summary>
        public int Unk1C { get; set; }

        IMsbModel IMsbModel.DeepCopy()
        {
            return DeepCopy();
        }

        /// <summary>
        ///     Creates a deep copy of the model.
        /// </summary>
        public Model DeepCopy()
        {
            return (Model)MemberwiseClone();
        }

        private protected virtual void ReadTypeData(BinaryReaderEx br)
        {
            throw new NotImplementedException($"Type {GetType()} missing valid {nameof(ReadTypeData)}.");
        }

        internal override void Write(BinaryWriterEx bw, int id)
        {
            var start = bw.Position;
            bw.ReserveInt64("NameOffset");
            bw.WriteUInt32((uint)Type);
            bw.WriteInt32(id);
            bw.ReserveInt64("SibOffset");
            bw.WriteInt32(InstanceCount);
            bw.WriteInt32(Unk1C);
            bw.ReserveInt64("TypeDataOffset");

            bw.FillInt64("NameOffset", bw.Position - start);
            bw.WriteUTF16(MSB.ReambiguateName(Name), true);
            bw.FillInt64("SibOffset", bw.Position - start);
            bw.WriteUTF16(SibPath, true);
            bw.Pad(8);

            if (HasTypeData)
            {
                bw.FillInt64("TypeDataOffset", bw.Position - start);
                WriteTypeData(bw);
            }
            else
            {
                bw.FillInt64("TypeDataOffset", 0);
            }
        }

        private protected virtual void WriteTypeData(BinaryWriterEx bw)
        {
            throw new NotImplementedException($"Type {GetType()} missing valid {nameof(ReadTypeData)}.");
        }

        internal void CountInstances(List<Part> parts)
        {
            InstanceCount = parts.Count(p => p.ModelName == Name);
        }

        /// <summary>
        ///     Returns the type and name of the model as a string.
        /// </summary>
        public override string ToString()
        {
            return $"{Type} {Name}";
        }

        /// <summary>
        ///     A model for fixed terrain or scenery.
        /// </summary>
        public class MapPiece : Model
        {

            /// <summary>
            ///     Creates a MapPiece with default values.
            /// </summary>
            public MapPiece() : base("mXXXXXX")
            {
            }

            internal MapPiece(BinaryReaderEx br) : base(br)
            {
            }

            private protected override ModelType Type => ModelType.MapPiece;
            private protected override bool HasTypeData => true;

            /// <summary>
            ///     Unknown.
            /// </summary>
            public bool UnkT00 { get; set; }

            /// <summary>
            ///     Unknown.
            /// </summary>
            public bool UnkT01 { get; set; }

            /// <summary>
            ///     Unknown.
            /// </summary>
            public bool UnkT02 { get; set; }

            /// <summary>
            ///     Unknown.
            /// </summary>
            public float UnkT04 { get; set; }

            /// <summary>
            ///     Unknown.
            /// </summary>
            public float UnkT08 { get; set; }

            /// <summary>
            ///     Unknown.
            /// </summary>
            public float UnkT0C { get; set; }

            /// <summary>
            ///     Unknown.
            /// </summary>
            public float UnkT10 { get; set; }

            /// <summary>
            ///     Unknown.
            /// </summary>
            public float UnkT14 { get; set; }

            /// <summary>
            ///     Unknown.
            /// </summary>
            public float UnkT18 { get; set; }

            private protected override void ReadTypeData(BinaryReaderEx br)
            {
                UnkT00 = br.ReadBoolean();
                UnkT01 = br.ReadBoolean();
                UnkT02 = br.ReadBoolean();
                br.AssertByte(0);
                UnkT04 = br.ReadSingle();
                UnkT08 = br.ReadSingle();
                UnkT0C = br.ReadSingle();
                UnkT10 = br.ReadSingle();
                UnkT14 = br.ReadSingle();
                UnkT18 = br.ReadSingle();
                br.AssertInt32(0);
            }

            private protected override void WriteTypeData(BinaryWriterEx bw)
            {
                bw.WriteBoolean(UnkT00);
                bw.WriteBoolean(UnkT01);
                bw.WriteBoolean(UnkT02);
                bw.WriteByte(0);
                bw.WriteSingle(UnkT04);
                bw.WriteSingle(UnkT08);
                bw.WriteSingle(UnkT0C);
                bw.WriteSingle(UnkT10);
                bw.WriteSingle(UnkT14);
                bw.WriteSingle(UnkT18);
                bw.WriteInt32(0);
            }
        }

        /// <summary>
        ///     A model for a dynamic prop.
        /// </summary>
        public class Object : Model
        {

            /// <summary>
            ///     Creates an Object with default values.
            /// </summary>
            public Object() : base("oXXXXXX")
            {
            }

            internal Object(BinaryReaderEx br) : base(br)
            {
            }

            private protected override ModelType Type => ModelType.Object;
            private protected override bool HasTypeData => false;
        }

        /// <summary>
        ///     A model for a non-player entity.
        /// </summary>
        public class Enemy : Model
        {

            /// <summary>
            ///     Creates an Enemy with default values.
            /// </summary>
            public Enemy() : base("cXXXX")
            {
            }

            internal Enemy(BinaryReaderEx br) : base(br)
            {
            }

            private protected override ModelType Type => ModelType.Enemy;
            private protected override bool HasTypeData => false;
        }

        /// <summary>
        ///     A model for a player spawn point?
        /// </summary>
        public class Player : Model
        {

            /// <summary>
            ///     Creates a Player with default values.
            /// </summary>
            public Player() : base("c0000")
            {
            }

            internal Player(BinaryReaderEx br) : base(br)
            {
            }

            private protected override ModelType Type => ModelType.Player;
            private protected override bool HasTypeData => false;
        }

        /// <summary>
        ///     A model for collision physics.
        /// </summary>
        public class Collision : Model
        {

            /// <summary>
            ///     Creates a Collision with default values.
            /// </summary>
            public Collision() : base("hXXXXXX")
            {
            }

            internal Collision(BinaryReaderEx br) : base(br)
            {
            }

            private protected override ModelType Type => ModelType.Collision;
            private protected override bool HasTypeData => false;
        }
    }
}