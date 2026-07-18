using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace VRGloveDataCapture.Capture
{
    internal sealed class Hi5CaptureAdapter
    {
        internal sealed class BoneBinding
        {
            public string Hand;
            public string Bone;
            public string ParentBone;
            public Transform Transform;
        }

        internal struct SensorStatus
        {
            public string Hand;
            public string Sensor;
            public int Online;
            public uint Energy;
            public uint Signal;
            public uint MagneticQuality;
        }

        private static readonly string[] SensorNames =
        {
            "hand", "thumb", "index", "middle", "ring", "pinky"
        };

        private readonly List<BoneBinding> bones = new List<BoneBinding>();
        private readonly Dictionary<Transform, string> boneNames = new Dictionary<Transform, string>();
        private Type inertiaInstanceType;
        private Type bonesEnumType;
        private FieldInfo handTypeField;
        private FieldInfo handBonesField;

        private Type managerThreadType;
        private MethodInfo managerInstanceMethod;
        private FieldInfo sensorCollectionField;
        private MethodInfo sensorGetMethod;
        private FieldInfo sensorOnlineField;
        private FieldInfo sensorEnergyField;
        private FieldInfo sensorSignalField;
        private FieldInfo sensorMagneticField;

        public IList<BoneBinding> Bones { get { return bones; } }

        public int RefreshBones()
        {
            bones.Clear();
            boneNames.Clear();

            if (!ResolveBoneReflection())
            {
                return 0;
            }

            UnityEngine.Object[] instances = Resources.FindObjectsOfTypeAll(inertiaInstanceType);
            for (int instanceIndex = 0; instanceIndex < instances.Length; instanceIndex++)
            {
                Component component = instances[instanceIndex] as Component;
                if (component == null || !component.gameObject.scene.IsValid())
                {
                    continue;
                }

                object handValue = handTypeField.GetValue(component);
                string hand = handValue == null ? "unknown" : handValue.ToString().ToLowerInvariant();
                Transform[] transforms = handBonesField.GetValue(component) as Transform[];
                if (transforms == null)
                {
                    continue;
                }

                for (int boneIndex = 0; boneIndex < transforms.Length; boneIndex++)
                {
                    Transform boneTransform = transforms[boneIndex];
                    if (boneTransform == null)
                    {
                        continue;
                    }

                    string boneName = Enum.GetName(bonesEnumType, boneIndex) ?? ("bone_" + boneIndex);
                    boneNames[boneTransform] = boneName;
                    bones.Add(new BoneBinding
                    {
                        Hand = hand,
                        Bone = boneName,
                        Transform = boneTransform
                    });
                }
            }

            for (int index = 0; index < bones.Count; index++)
            {
                Transform parent = bones[index].Transform.parent;
                string parentName;
                bones[index].ParentBone = parent != null && boneNames.TryGetValue(parent, out parentName)
                    ? parentName
                    : string.Empty;
            }

            bones.Sort(delegate(BoneBinding left, BoneBinding right)
            {
                int handComparison = string.CompareOrdinal(left.Hand, right.Hand);
                return handComparison != 0
                    ? handComparison
                    : string.CompareOrdinal(left.Bone, right.Bone);
            });
            return bones.Count;
        }

        public int ReadSensorStatuses(List<SensorStatus> destination)
        {
            destination.Clear();
            if (!ResolveSensorReflection())
            {
                return 0;
            }

            try
            {
                object manager = managerInstanceMethod.Invoke(null, null);
                object sensorCollection = manager == null ? null : sensorCollectionField.GetValue(manager);
                if (sensorCollection == null)
                {
                    return 0;
                }

                for (int index = 0; index < 12; index++)
                {
                    object sensor = sensorGetMethod.Invoke(sensorCollection, new object[] { index });
                    if (sensor == null)
                    {
                        continue;
                    }

                    int handOffset = index < 6 ? index : index - 6;
                    destination.Add(new SensorStatus
                    {
                        Hand = index < 6 ? "right" : "left",
                        Sensor = SensorNames[handOffset],
                        Online = Convert.ToInt32(sensorOnlineField.GetValue(sensor)),
                        Energy = Convert.ToUInt32(sensorEnergyField.GetValue(sensor)),
                        Signal = Convert.ToUInt32(sensorSignalField.GetValue(sensor)),
                        MagneticQuality = Convert.ToUInt32(sensorMagneticField.GetValue(sensor))
                    });
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[DataCapture] Failed to read Hi5 sensor status: " + exception.Message);
            }

            return destination.Count;
        }

        private bool ResolveBoneReflection()
        {
            if (inertiaInstanceType != null)
            {
                return handTypeField != null && handBonesField != null && bonesEnumType != null;
            }

            inertiaInstanceType = FindType("HI5.HI5_InertiaInstance");
            bonesEnumType = FindType("HI5.Bones");
            if (inertiaInstanceType == null || bonesEnumType == null)
            {
                return false;
            }

            handTypeField = FindField(inertiaInstanceType, "HandType");
            handBonesField = FindField(inertiaInstanceType, "HandBones");
            return handTypeField != null && handBonesField != null;
        }

        private bool ResolveSensorReflection()
        {
            if (managerThreadType != null)
            {
                return managerInstanceMethod != null && sensorCollectionField != null && sensorGetMethod != null;
            }

            managerThreadType = FindType("HI5.HI5_Manager_Thread");
            if (managerThreadType == null)
            {
                return false;
            }

            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic |
                                     BindingFlags.Instance | BindingFlags.Static;
            managerInstanceMethod = managerThreadType.GetMethod("Instance", all);
            sensorCollectionField = managerThreadType.GetField("_sensorInfor", all);
            if (sensorCollectionField == null)
            {
                return false;
            }

            Type collectionType = sensorCollectionField.FieldType;
            sensorGetMethod = collectionType.GetMethod("Get", all);
            Type sensorType = FindType("HI5.SensorInfor");
            if (sensorType == null)
            {
                return false;
            }

            sensorOnlineField = sensorType.GetField("_online", all);
            sensorEnergyField = sensorType.GetField("_energyValue", all);
            sensorSignalField = sensorType.GetField("_signalValue", all);
            sensorMagneticField = sensorType.GetField("_magneticValue", all);
            return managerInstanceMethod != null && sensorGetMethod != null &&
                   sensorOnlineField != null && sensorEnergyField != null &&
                   sensorSignalField != null && sensorMagneticField != null;
        }

        private static Type FindType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int index = 0; index < assemblies.Length; index++)
            {
                Type type = assemblies[index].GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static FieldInfo FindField(Type type, string name)
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            Type current = type;
            while (current != null)
            {
                FieldInfo field = current.GetField(name, all);
                if (field != null)
                {
                    return field;
                }
                current = current.BaseType;
            }
            return null;
        }
    }
}
