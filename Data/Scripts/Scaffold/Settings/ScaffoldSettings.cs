using System;
using System.Xml.Serialization;
using Sandbox.ModAPI;
using ScaffoldMod.Utility;
using VRage.Serialization;

namespace ScaffoldMod.Settings
{
    public enum BuildPatternEnum : byte
    {
        FromProjector,
        FromCenter,
        FromCorners,
    }

    public struct YardSettingsStruct
    {
        public YardSettingsStruct(long entityId)
        {
            EntityId = entityId;
            BeamCount = 3;
            GuideEnabled = true;
            WeldMultiplier = 0.1f;
            GrindMultiplier = 0.1f;
            AdvancedLocking = false;
            BuildPattern = BuildPatternEnum.FromProjector;
        }

        public readonly long EntityId;
        public int BeamCount;
        public bool GuideEnabled;
        public float WeldMultiplier;
        public float GrindMultiplier;
        public bool AdvancedLocking;
        public BuildPatternEnum BuildPattern;
    }

    [XmlInclude(typeof(YardSettingsStruct))]
    public class ScaffoldSettings
    {
        private static ScaffoldSettings _instance;

        public SerializableDictionary<long, YardSettingsStruct> BlockSettings;

        public ScaffoldSettings()
        {
            BlockSettings = new SerializableDictionary<long, YardSettingsStruct>();
        }

        public static ScaffoldSettings Instance
        {
            get
            {
                if (_instance != null)
                    return _instance;

                if (!Load())
                    _instance = new ScaffoldSettings();

                return _instance;
            }
        }

        public YardSettingsStruct GetYardSettings(long entityId)
        {
            YardSettingsStruct result;
            if (!BlockSettings.Dictionary.TryGetValue(entityId, out result))
            {
                result = new YardSettingsStruct(entityId);
                SetYardSettings(entityId, result);
            }
            return result;
        }

        public void SetYardSettings(long entityId, YardSettingsStruct newSet)
        {
            BlockSettings[entityId] = newSet;
        }

        public void Save()
        {
            Logging.Instance.WriteLine("Saving settings");
            string serialized = MyAPIGateway.Utilities.SerializeToXML(this);
            MyAPIGateway.Utilities.SetVariable("ScaffoldSettings", serialized);
            Logging.Instance.WriteLine("Done saving settings");
        }

        private static bool Load()
        {
            Logging.Instance.WriteLine("Loading settings");
            try
            {
                string value;
                if (!MyAPIGateway.Utilities.GetVariable("ScaffoldSettings", out value))
                {
                    Logging.Instance.WriteLine("Settings do not exist in world file");
                    return false;
                }
                _instance = MyAPIGateway.Utilities.SerializeFromXML<ScaffoldSettings>(value);
                Logging.Instance.WriteLine("Done loading settings");
                return true;
            }
            catch (Exception ex)
            {
                Logging.Instance.WriteLine("Error loading settings: " + ex);
                return false;
            }
        }
    }
}