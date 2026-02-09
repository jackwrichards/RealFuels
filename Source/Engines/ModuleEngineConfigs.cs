using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Linq;
using UnityEngine;
using RealFuels.Tanks;
using RealFuels.TechLevels;
using KSP.UI.Screens;
using KSP.Localization;
using Debug = UnityEngine.Debug;

namespace RealFuels
{
    public struct Gimbal
    {
        public float gimbalRange;
        public float gimbalRangeXP;
        public float gimbalRangeXN;
        public float gimbalRangeYP;
        public float gimbalRangeYN;

        public Gimbal(float gimbalRange, float gimbalRangeXP, float gimbalRangeXN, float gimbalRangeYP, float gimbalRangeYN)
        {
            this.gimbalRange = gimbalRange;
            this.gimbalRangeXP = gimbalRangeXP;
            this.gimbalRangeXN = gimbalRangeXN;
            this.gimbalRangeYP = gimbalRangeYP;
            this.gimbalRangeYN = gimbalRangeYN;
        }

        public string Info()
        {
            if (new[] { gimbalRange, gimbalRangeXP, gimbalRangeXN, gimbalRangeYP, gimbalRangeYN }.Distinct().Count() == 1)
                return $"{gimbalRange:0.#}°";
            if (new[] { gimbalRangeXP, gimbalRangeXN, gimbalRangeYP, gimbalRangeYN }.Distinct().Count() == 1)
                return $"{gimbalRangeXP:0.#}°";
            var ret = string.Empty;
            if (gimbalRangeXP == gimbalRangeXN)
                ret += $"{gimbalRangeXP:0.#}° pitch, ";
            else
                ret += $"+{gimbalRangeXP:0.#}°/-{gimbalRangeXN:0.#}° pitch, ";
            if (gimbalRangeYP == gimbalRangeYN)
                ret += $"{gimbalRangeYP:0.#}° yaw";
            else
                ret += $"+{gimbalRangeYP:0.#}°/-{gimbalRangeYN:0.#}° yaw";
            return ret;
        }
    }

    public class ModuleEngineConfigs : ModuleEngineConfigsBase
    {
        public const string PatchNodeName = "SUBCONFIG";
        protected const string PatchNameKey = "__mpecPatchName";

        [KSPField(isPersistant = true)]
        public string activePatchName = "";

        [KSPField(isPersistant = true)]
        public bool dynamicPatchApplied = false;

        protected bool ConfigHasPatch(ConfigNode config) => GetPatchesOfConfig(config).Count > 0;

        protected List<ConfigNode> GetPatchesOfConfig(ConfigNode config)
        {
            ConfigNode[] list = config.GetNodes(PatchNodeName);
            List<ConfigNode> sortedList = ConfigFilters.Instance.FilterDisplayConfigs(list.ToList());
            return sortedList;
        }

        protected ConfigNode GetPatch(string configName, string patchName)
        {
            return GetPatchesOfConfig(GetConfigByName(configName))
                .FirstOrDefault(patch => patch.GetValue("name") == patchName);
        }

        protected bool ConfigIsPatched(ConfigNode config) => config.HasValue(PatchNameKey);

        // TODO: This is called a lot, performance concern?
        protected ConfigNode PatchConfig(ConfigNode parentConfig, ConfigNode patch, bool dynamic)
        {
            var patchedNode = parentConfig.CreateCopy();

            foreach (var key in patch.values.DistinctNames())
                patchedNode.RemoveValues(key);
            foreach (var nodeName in patch.nodes.DistinctNames())
                patchedNode.RemoveNodes(nodeName);

            patch.CopyTo(patchedNode);

            // Apply cost offset
            int costOffset = 0;
            patch.TryGetValue("costOffset", ref costOffset);
            int cost = 0;
            patchedNode.TryGetValue("cost", ref cost);
            cost += costOffset;
            patchedNode.SetValue("cost", cost, true);

            patchedNode.SetValue("name", parentConfig.GetValue("name"));
            if (!dynamic)
                patchedNode.AddValue(PatchNameKey, patch.GetValue("name"));
            return patchedNode;
        }

        public ConfigNode GetNonDynamicPatchedConfiguration() => GetSetConfigurationTarget(configuration);

        public void ApplyDynamicPatch(ConfigNode patch)
        {
            // Debug.Log($"**RFMPEC** dynamic patch applied to active config `{configurationDisplay}`");
            SetConfiguration(PatchConfig(GetNonDynamicPatchedConfiguration(), patch, true), false);
            dynamicPatchApplied = true;
        }

        protected override ConfigNode GetSetConfigurationTarget(string newConfiguration)
        {
            if (activePatchName == "")
                return base.GetSetConfigurationTarget(newConfiguration);
            return PatchConfig(GetConfigByName(newConfiguration), GetPatch(newConfiguration, activePatchName), false);
        }

        public override void SetConfiguration(string newConfiguration = null, bool resetTechLevels = false)
        {
            base.SetConfiguration(newConfiguration, resetTechLevels);
            if (dynamicPatchApplied)
            {
                dynamicPatchApplied = false;
                part.SendMessage("OnMPECDynamicPatchReset", SendMessageOptions.DontRequireReceiver);
            }
        }

        public override int UpdateSymmetryCounterparts()
        {
            DoForEachSymmetryCounterpart((engine) =>
                (engine as ModuleEngineConfigs).activePatchName = activePatchName);
            return base.UpdateSymmetryCounterparts();
        }

        public override string GetConfigInfo(ConfigNode config, bool addDescription = true, bool colorName = false)
        {
            var info = base.GetConfigInfo(config, addDescription, colorName);

            if (!ConfigHasPatch(config) || ConfigIsPatched(config))
                return info;

            if (addDescription) info += "\n";
            foreach (var patch in GetPatchesOfConfig(config))
                info += ConfigInfoString(PatchConfig(config, patch, false), false, colorName);
            return info;
        }

        public override string GetConfigDisplayName(ConfigNode node)
        {
            if (node.HasValue("displayName"))
                return node.GetValue("displayName");
            var name = node.GetValue("name");
            if (!node.HasValue(PatchNameKey))
                 return name;
            return node.GetValue(PatchNameKey); // Just show subconfig name without parent prefix
        }

        protected override IEnumerable<ConfigRowDefinition> BuildConfigRows()
        {
            foreach (var node in FilteredDisplayConfigs(false))
            {
                string configName = node.GetValue("name");
                yield return new ConfigRowDefinition
                {
                    Node = node,
                    DisplayName = GetConfigDisplayName(node),
                    IsSelected = configName == configuration && activePatchName == "",
                    Indent = false,
                    Apply = () =>
                    {
                        activePatchName = "";
                        GUIApplyConfig(configName);
                    }
                };

                foreach (var patch in GetPatchesOfConfig(node))
                {
                    var patchedNode = PatchConfig(node, patch, false);
                    string patchName = patch.GetValue("name");
                    string patchedConfigName = configName;
                    yield return new ConfigRowDefinition
                    {
                        Node = patchedNode,
                        DisplayName = GetConfigDisplayName(patchedNode),
                        IsSelected = patchedConfigName == configuration && patchName == activePatchName,
                        Indent = true,
                        Apply = () =>
                        {
                            activePatchName = patchName;
                            GUIApplyConfig(patchedConfigName);
                        }
                    };
                }
            }
        }

        protected override void DrawConfigSelectors(IEnumerable<ConfigNode> availableConfigNodes)
        {
            DrawConfigTable(BuildConfigRows());
        }
    }

    public class ModuleEngineConfigsBase : PartModule, IPartCostModifier, IPartMassModifier
    {
        //protected const string groupName = "ModuleEngineConfigs";
        public const string groupName = ModuleEnginesRF.groupName;
        public const string groupDisplayName = "#RF_Engine_EngineConfigs"; // "Engine Configs"
        #region Fields
        protected bool compatible = true;

        [KSPField(isPersistant = true)]
        public string configuration = string.Empty;

        // For display purposes only.
        [KSPField(guiName = "#RF_Engine_Configuration", isPersistant = true, guiActiveEditor = true, guiActive = true, // Configuration
            groupName = groupName, groupDisplayName = groupDisplayName)]
        public string configurationDisplay = string.Empty;

        // Tech Level stuff
        [KSPField(isPersistant = true)]
        public int techLevel = -1; // default: disable

        [KSPField]
        public int origTechLevel = -1; // default TL, starts disabled

        [KSPField]
        public float origMass = -1;
        protected float massDelta = 0;

        public int? Ignitions { get; protected set; }

        [KSPField]
        public string gimbalTransform = string.Empty;
        [KSPField]
        public float gimbalMult = 1f;
        [KSPField]
        public bool useGimbalAnyway = false;

        private Dictionary<string, Gimbal> defaultGimbals = null;

        [KSPField]
        public bool autoUnlock = true;

        [KSPField]
        public int maxTechLevel = -1;
        [KSPField]
        public int minTechLevel = -1;

        [KSPField]
        public string engineType = "L"; // default = lower stage

        [KSPField]
        public float throttle = 0.0f; // default min throttle level
        public float configThrottle;

        public string configDescription = string.Empty;

        public ConfigNode techNodes;

        [KSPField]
        public bool isMaster = true; //is this Module the "master" module on the part? (if false, don't do GUI)
        // For TestFlight integration, only ONE ModuleEngineConfigs (or child class) can be master module on a part.

        [KSPField]
        public string type = "ModuleEnginesRF";
        [KSPField]
        public bool useWeakType = true; // match any ModuleEngines*

        [KSPField]
        public string engineID = string.Empty;

        [KSPField]
        public int moduleIndex = -1;

        [KSPField]
        public int offsetGUIPos = -1;

        [KSPField(isPersistant = true)]
        public string thrustRating = "maxThrust";

        [KSPField(isPersistant = true)]
        public bool modded = false;

        [KSPField]
        public bool literalZeroIgnitions = false; /* Normally, ignitions = 0 means unlimited.  Setting this changes it to really mean zero */

        public List<ConfigNode> configs;
        internal List<ConfigNode> filteredDisplayConfigs;
        public ConfigNode config;

        public static Dictionary<string, string> techNameToTitle = new Dictionary<string, string>();

        // KIDS integration
        public static float ispSLMult = 1.0f;
        public static float ispVMult = 1.0f;

        [KSPField]
        public bool useConfigAsTitle = false;

        public float configMaxThrust = 1.0f;
        public float configMinThrust = 0.0f;
        public float configMassMult = 1.0f;
        public float configHeat = 0.0f;
        public float configCost = 0f;
        public float scale = 1f;
        #endregion

        #region Callbacks
        public float GetModuleCost(float stdCost, ModifierStagingSituation sit) => configCost;
        public ModifierChangeWhen GetModuleCostChangeWhen() => ModifierChangeWhen.FIXED;

        public float GetModuleMass(float defaultMass, ModifierStagingSituation sit) => massDelta;
        public ModifierChangeWhen GetModuleMassChangeWhen() => ModifierChangeWhen.FIXED;

        [KSPEvent(guiActive = false, active = true)]
        void OnPartScaleChanged(BaseEventDetails data)
        {
            float factorAbsolute = data.Get<float>("factorAbsolute");
            float factorRelative = data.Get<float>("factorRelative");
            scale = factorAbsolute * factorAbsolute; // quadratic
            SetConfiguration();
            //Debug.Log($"[RFMEC] OnPartScaleChanged for {part}: factorRelative={factorRelative} | factorAbsolute={factorAbsolute}");
        }
        #endregion

        public static void BuildTechNodeMap()
        {
            if (techNameToTitle?.Count == 0)
            {
                string fullPath = KSPUtil.ApplicationRootPath + HighLogic.CurrentGame.Parameters.Career.TechTreeUrl;
                ConfigNode treeNode = new ConfigNode();
                if (ConfigNode.Load(fullPath) is ConfigNode fileNode && fileNode.TryGetNode("TechTree", ref treeNode))
                {
                    foreach (ConfigNode n in treeNode.GetNodes("RDNode"))
                    {
                        if (n.HasValue("id") && n.HasValue("title"))
                            techNameToTitle[n.GetValue("id")] = n.GetValue("title");
                    }
                }
            }
        }

        private void LoadDefaultGimbals()
        {
            defaultGimbals = new Dictionary<string, Gimbal>();
            foreach (var g in part.Modules.OfType<ModuleGimbal>())
                defaultGimbals[g.gimbalTransformName] = new Gimbal(g.gimbalRange, g.gimbalRangeXP, g.gimbalRangeXN, g.gimbalRangeYP, g.gimbalRangeYN);
        }

        public static void RelocateRCSPawItems(ModuleRCS module)
        {
            var field = module.Fields["thrusterPower"];
            field.guiActive = true;
            field.guiActiveEditor = true;
            field.guiName = Localizer.GetStringByTag("#RF_Engine_ThrusterPower"); // Thruster Power
            field.guiUnits = "kN";
            field.group = new BasePAWGroup(groupName, groupDisplayName, false);
        }

        protected List<ConfigNode> FilteredDisplayConfigs(bool update)
        {
            if (update || filteredDisplayConfigs == null)
            {
                filteredDisplayConfigs = ConfigFilters.Instance.FilterDisplayConfigs(configs);
            }
            return filteredDisplayConfigs;
        }

        #region PartModule Overrides
        public override void OnAwake()
        {
            techNodes = new ConfigNode();
            configs = new List<ConfigNode>();
        }

        public override void OnLoad(ConfigNode node)
        {
            if (!compatible)
                return;
            base.OnLoad(node);

            if (techLevel != -1)
            {
                if (maxTechLevel < 0)
                    maxTechLevel = TechLevel.MaxTL(node, engineType);
                if (minTechLevel < 0)
                    minTechLevel = Math.Min(origTechLevel, techLevel);
            }

            if (origMass > 0)
            {
                part.mass = origMass * RFSettings.Instance.EngineMassMultiplier;
                massDelta = (part?.partInfo?.partPrefab is Part p) ? part.mass - p.mass : 0;
            }

            if (node.GetNodes("CONFIG") is ConfigNode[] cNodes && cNodes.Length > 0)
            {
                configs.Clear();
                foreach (ConfigNode subNode in cNodes)
                {
                    //Debug.Log("*RFMEC* Load Engine Configs. Part " + part.name + " has config " + subNode.GetValue("name"));
                    ConfigNode newNode = new ConfigNode("CONFIG");
                    subNode.CopyTo(newNode);
                    configs.Add(newNode);
                }
            }

            foreach (ConfigNode n in node.GetNodes("TECHLEVEL"))
                techNodes.AddNode(n);

            ConfigSaveLoad();

            SetConfiguration();
        }

        public override void OnStart(StartState state)
        {
            if (!compatible)
                return;
            enabled = true;
            BuildTechNodeMap();

            Fields[nameof(showRFGUI)].guiActiveEditor = isMaster;
            if (HighLogic.LoadedSceneIsEditor)
            {
                GameEvents.onPartActionUIDismiss.Add(OnPartActionGuiDismiss);
                GameEvents.onPartActionUIShown.Add(OnPartActionUIShown);
            }

            ConfigSaveLoad();

            Integrations.LoadB9PSModules();

            LoadDefaultGimbals();

            SetConfiguration();

            Fields[nameof(showRFGUI)].guiName = GUIButtonName;

            // Why is this here, if KSP will call this normally?
            part.Modules.GetModule("ModuleEngineIgnitor")?.OnStart(state);
        }

        public override void OnStartFinished(StartState state)
        {
            Integrations.HideB9PSVariantSelectors();
            if (pModule is ModuleRCS mrcs) RelocateRCSPawItems(mrcs);
        }
        #endregion

        #region Info Methods
        private string TLTInfo()
        {
            string retStr = string.Empty;
            if (engineID != string.Empty)
                retStr += $"{Localizer.Format("#RF_Engine_BoundToEngineID", engineID)}\n"; // (Bound to {engineID})
            if (moduleIndex >= 0)
                retStr += $"{Localizer.Format("#RF_Engine_BoundToModuleIndex", moduleIndex)}\n"; // (Bound to engine {moduleIndex} in part)
            if (techLevel != -1)
            {
                TechLevel cTL = new TechLevel();
                if (!cTL.Load(config, techNodes, engineType, techLevel))
                    cTL = null;

                if (!string.IsNullOrEmpty(configDescription))
                    retStr += configDescription + "\n";

                retStr += $"{Localizer.GetStringByTag("#RF_Engine_TLTInfo_Type")}: {engineType}. {Localizer.GetStringByTag("#RF_Engine_TLTInfo_TechLevel")}: {techLevel} ({origTechLevel}-{maxTechLevel})"; // TypeTech Level
                if (origMass > 0)
                    retStr += $", {Localizer.Format("#RF_Engine_TLTInfo_OrigMass", $"{part.mass:N3}", $"{origMass * RFSettings.Instance.EngineMassMultiplier:N3}")}"; // Mass: {part.mass:N3} (was {origMass * RFSettings.Instance.EngineMassMultiplier:N3})
                if (configThrottle >= 0)
                    retStr += $", {Localizer.GetStringByTag("#RF_Engine_TLTInfo_MinThrust")} {configThrottle:P0}"; // MinThr

                float gimbalR = -1f;
                if (config.HasValue("gimbalRange"))
                    gimbalR = float.Parse(config.GetValue("gimbalRange"), CultureInfo.InvariantCulture);
                else if (!gimbalTransform.Equals(string.Empty) || useGimbalAnyway)
                {
                    if (cTL != null)
                        gimbalR = cTL.GimbalRange;
                }
                if (gimbalR != -1f)
                    retStr += $", {Localizer.GetStringByTag("#RF_Engine_TLTInfo_Gimbal")} {gimbalR:N1}"; // Gimbal
            }
            return retStr;
        }

        virtual public string GetConfigDisplayName(ConfigNode node) => node.GetValue("name");
        public override string GetInfo()
        {
            if (!compatible)
                return string.Empty;
            var configsToDisplay = FilteredDisplayConfigs(true);
            if (configsToDisplay.Count < 2)
                return TLTInfo();

            string info = TLTInfo() + $"\n{Localizer.GetStringByTag("#RF_Engine_AlternateConfigurations")}:\n"; // Alternate configurations

            foreach (ConfigNode config in configsToDisplay)
                if (!config.GetValue("name").Equals(configuration))
                    info += GetConfigInfo(config, addDescription: false, colorName: true);

            return info;
        }

        protected string ConfigInfoString(ConfigNode config, bool addDescription, bool colorName)
        {
            TechLevel cTL = new TechLevel();
            if (!cTL.Load(config, techNodes, engineType, techLevel))
                cTL = null;
            var info = StringBuilderCache.Acquire();

            if (colorName)
                info.Append("<color=green>");
            info.Append(GetConfigDisplayName(config));
            if (colorName)
                info.Append("</color>");
            info.Append("\n");

            if (config.HasValue(thrustRating))
            {
                info.Append($"  {Utilities.FormatThrust(scale * TechLevels.ThrustTL(config.GetValue(thrustRating), config))}");
                // add throttling info if present
                if (config.HasValue("minThrust"))
                    info.Append($", {Localizer.GetStringByTag("#RF_Engine_minThrustInfo")} {float.Parse(config.GetValue("minThrust"), CultureInfo.InvariantCulture) / float.Parse(config.GetValue(thrustRating), CultureInfo.InvariantCulture):P0}"); //min
                else if (config.HasValue("throttle"))
                    info.Append($", {Localizer.GetStringByTag("#RF_Engine_minThrustInfo")} {float.Parse(config.GetValue("throttle"), CultureInfo.InvariantCulture):P0}"); // min
            }
            else
                info.Append($"  {Localizer.GetStringByTag("#RF_Engine_UnknownThrust")}"); // Unknown Thrust

            if (origMass > 0f)
            {
                float cMass = scale * origMass * RFSettings.Instance.EngineMassMultiplier;
                if (config.HasValue("massMult") && float.TryParse(config.GetValue("massMult"), out float ftmp))
                    cMass *= ftmp;

                info.Append($", {cMass:N3}t");
            }
            info.Append("\n");

            if (config.HasNode("atmosphereCurve"))
            {
                FloatCurve isp = new FloatCurve();
                isp.Load(config.GetNode("atmosphereCurve"));
                info.Append($"  {Localizer.GetStringByTag("#RF_Engine_Isp")}: {isp.Evaluate(isp.maxTime)} - {isp.Evaluate(isp.minTime)}s\n"); // Isp
            }
            else if (config.HasValue("IspSL") && config.HasValue("IspV") && cTL != null)
            {
                float.TryParse(config.GetValue("IspSL"), out float ispSL);
                float.TryParse(config.GetValue("IspV"), out float ispV);
                ispSL *= ispSLMult * cTL.AtmosphereCurve.Evaluate(1);
                ispV *= ispVMult * cTL.AtmosphereCurve.Evaluate(0);
                info.Append($"  {Localizer.GetStringByTag("#RF_Engine_Isp")}: {ispSL:N0} - {ispV:N0}s\n"); // Isp
            }

            if (config.HasNode("PROPELLANT"))
            {
                var propellants = config.GetNodes("PROPELLANT")
                    .Select(node =>
                    {
                        string name = node.GetValue("name");
                        string ratioStr = null;
                        if (node.TryGetValue("ratio", ref ratioStr) && float.TryParse(ratioStr, out float ratio))
                            return $"{name} ({ratio:N3})";
                        return name;
                    })
                    .Where(name => !string.IsNullOrWhiteSpace(name));

                string propellantList = string.Join(", ", propellants);
                if (!string.IsNullOrWhiteSpace(propellantList))
                    info.Append($"  {Localizer.GetStringByTag("#RF_EngineRF_Propellant")}: {propellantList}\n");
            }

            if (config.HasValue("ratedBurnTime"))
            {
                if (config.HasValue("ratedContinuousBurnTime"))
                    info.Append($"  {Localizer.GetStringByTag("#RF_Engine_RatedBurnTime")}: {config.GetValue("ratedContinuousBurnTime")}/{config.GetValue("ratedBurnTime")}s\n"); // Rated burn time
                else
                    info.Append($"  {Localizer.GetStringByTag("#RF_Engine_RatedBurnTime")}: {config.GetValue("ratedBurnTime")}s\n"); // Rated burn time
            }

            if (part.HasModuleImplementing<ModuleGimbal>())
            {
                if (config.HasNode("GIMBAL"))
                {
                    foreach (KeyValuePair<string, Gimbal> kv in ExtractGimbals(config))
                    {
                        info.Append($"  {Localizer.GetStringByTag("#RF_Engine_TLTInfo_Gimbal")} ({kv.Key}): {kv.Value.Info()}\n"); // Gimbal
                    }
                }
                else if (config.HasValue("gimbalRange"))
                {
                    // The extracted gimbals contain `gimbalRange` et al. applied to either a specific
                    // transform or all the gimbal transforms on the part. Either way, the values
                    // are all the same, so just take the first one.
                    var gimbal = ExtractGimbals(config).Values.First();
                    info.Append($"  Gimbal {gimbal.Info()}\n"); // 
                }
            }

            if (config.HasValue("ullage") || config.HasValue("ignitions") || config.HasValue("pressureFed"))
            {
                info.Append("  ");
                bool comma = false;
                if (config.HasValue("ullage"))
                {
                    info.Append(config.GetValue("ullage").ToLower() == "true" ? Localizer.GetStringByTag("#RF_Engine_ullage") : Localizer.GetStringByTag("#RF_Engine_NoUllage")); // "ullage""no ullage"
                    comma = true;
                }
                if (config.HasValue("pressureFed") && config.GetValue("pressureFed").ToLower() == "true")
                {
                    if (comma)
                        info.Append(", ");
                    info.Append(Localizer.GetStringByTag("#RF_Engine_pressureFed")); // "pfed"
                    comma = true;
                }

                if (config.HasValue("ignitions"))
                {
                    if (int.TryParse(config.GetValue("ignitions"), out int ignitions))
                    {
                        if (comma)
                            info.Append(", ");
                        if (ignitions > 0)
                            info.Append(Localizer.Format("#RF_Engine_ignitionsleft", ignitions)); // $"{ignitions} ignition{(ignitions > 1 ? "s" : string.Empty)}"
                        else if (literalZeroIgnitions && ignitions == 0)
                            info.Append(Localizer.GetStringByTag("#RF_Engine_GroundIgnitionOnly")); // "ground ignition only"
                        else
                            info.Append(Localizer.GetStringByTag("#RF_Engine_unlignitions")); // "unl. ignitions"
                    }
                }
                info.Append("\n");
            }
            if (config.HasValue("cost") && float.TryParse(config.GetValue("cost"), out float cst))
                info.Append($"  ({scale * cst:N0}√ {Localizer.GetStringByTag("#RF_Engine_extraCost")} )\n"); // extra cost// FIXME should get cost from TL, but this should be safe

            if (addDescription && config.HasValue("description"))
                info.Append($"\n  {config.GetValue("description")}\n");

            return info.ToStringAndRelease();
        }

        virtual public string GetConfigInfo(ConfigNode config, bool addDescription = true, bool colorName = false)
        {
            return ConfigInfoString(config, addDescription, colorName);
        }
        #endregion

        #region FX handling
        // Stop all effects registered with any config, but not with the current config
        private readonly HashSet<string> effectsToStop = new HashSet<string>();
        public void SetupFX()
        {
            List<string> effectsNames = new List<string>
            {
                "runningEffectName",
                "powerEffectName",
                "directThrottleEffectName",
                "disengageEffectName",
                "engageEffectName",
                "flameoutEffectName"
            };

            string val = string.Empty;
            IEnumerable<ConfigNode> others = configs.Where(x => !x.GetValue("name").Equals(configuration));
            ConfigNode ours = GetConfigByName(configuration);
            foreach (string fxName in effectsNames)
            {
                foreach (ConfigNode cfg in others)
                    if (cfg.TryGetValue(fxName, ref val))
                        effectsToStop.Add(val);
                if (ours is ConfigNode && ours.TryGetValue(fxName, ref val))
                    effectsToStop.Remove(val);
            }
        }
        public void StopFX()
        {
            foreach (var x in effectsToStop)
                part?.Effect(x, 0f);
        }
        #endregion

        #region Configuration
        public PartModule pModule = null;
        protected ConfigNode GetConfigByName(string name) => configs.Find(c => c.GetValue("name") == name);

        protected void SetConfiguration(ConfigNode newConfig, bool resetTechLevels)
        {
            string newConfiguration = newConfig.GetValue("name");

            if (configuration != newConfiguration)
            {
                if (resetTechLevels)
                    techLevel = origTechLevel;

                while (techLevel > 0 && !TechLevel.CanTL(newConfig, techNodes, engineType, techLevel))
                    --techLevel;
            }

            // for asmi
            if (useConfigAsTitle)
                part.partInfo.title = configuration;

            configuration = newConfiguration;
            configurationDisplay = GetConfigDisplayName(newConfig);
            config = new ConfigNode("MODULE");
            newConfig.CopyTo(config);
            config.name = "MODULE";

            if ((pModule = GetSpecifiedModule(part, engineID, moduleIndex, type, useWeakType)) is null)
            {
                Debug.LogError($"*RFMEC* Could not find appropriate module of type {type}, with ID={engineID} and index {moduleIndex}");
                return;
            }

            Type mType = pModule.GetType();
            config.SetValue("name", mType.Name);

            EngineConfigPropellants.ClearFloatCurves(mType, pModule, config, techLevel);
            EngineConfigPropellants.ClearPropellantGauges(mType, pModule);

            if (type.Equals("ModuleRCS") || type.Equals("ModuleRCSFX"))
                EngineConfigPropellants.ClearRCSPropellants(part, config, DoConfig);
            else
            { // is an ENGINE
                if (pModule is ModuleEngines mE && config.HasNode("PROPELLANT"))
                    mE.propellants.Clear();

                DoConfig(config);

                HandleEngineIgnitor(config);

                Ignitions = null;
                if (config.HasValue("ignitions"))
                {
                    if (int.TryParse(config.GetValue("ignitions"), out int tmpIgnitions))
                    {
                        Ignitions = TechLevels.ConfigIgnitions(tmpIgnitions);
                        config.SetValue("ignitions", Ignitions.Value);
                    }

                    if (HighLogic.LoadedSceneIsFlight && vessel?.situation != Vessel.Situations.PRELAUNCH)
                        config.RemoveValue("ignitions");
                }

                // Trigger re-computation of the response rate if one is not set explicitly.
                if (!config.HasValue("throttleResponseRate")) config.AddValue("throttleResponseRate", 0.0);

                if (pModule is ModuleEnginesRF)
                    (pModule as ModuleEnginesRF).SetScale(1d);
                pModule.Load(config);
            }
            // fix for editor NaN
            if (part.Resources.Contains("ElectricCharge") && part.Resources["ElectricCharge"].maxAmount < 0.1)
            { // hacking around a KSP bug here
                part.Resources["ElectricCharge"].amount = 0;
                part.Resources["ElectricCharge"].maxAmount = 0.1;
            }

            SetGimbalRange(config);

            if (!config.TryGetValue("cost", ref configCost))
                configCost = 0;
            if (!config.TryGetValue("description", ref configDescription))
                configDescription = string.Empty;

            UpdateOtherModules(config);

            // GUI disabled for now - UpdateTweakableMenu();

            // Prior comments suggest firing GameEvents.onEditorShipModified causes problems?
            part.SendMessage("OnEngineConfigurationChanged", SendMessageOptions.DontRequireReceiver);

            if (HighLogic.LoadedSceneIsEditor && EditorLogic.fetch.ship != null)
            {
                EditorPartSetMaintainer.Instance.ScheduleUsedBySetsUpdate();
            }

            SetupFX();

            Integrations.UpdateB9PSVariants();

            Integrations.UpdateTFInterops(); // update TestFlight if it's installed

            StopFX();
        }

        /// Allows subclasses to determine the configuration to switch to based on additional info.
        /// Used by MPEC to inject the patch if necessary.
        virtual protected ConfigNode GetSetConfigurationTarget(string newConfiguration) => GetConfigByName(newConfiguration);

        virtual public void SetConfiguration(string newConfiguration = null, bool resetTechLevels = false)
        {
            if (newConfiguration == null)
                newConfiguration = configuration;

            ConfigSaveLoad();

            if (configs.Count == 0)
            {
                Debug.LogError($"*RFMEC* configuration set was empty for {part}!");
                StopFX();
                return;
            }

            ConfigNode newConfig = GetSetConfigurationTarget(newConfiguration);
            if (!(newConfig is ConfigNode))
            {
                newConfig = configs.First();
                string s = newConfig.GetValue("name");
                Debug.LogWarning($"*RFMEC* WARNING could not find configuration \"{newConfiguration}\" for part {part.name}: Fallback to \"{s}\".");
                newConfiguration = s;
            }

            SetConfiguration(newConfig, resetTechLevels);
        }

        #region SetConfiguration Tools
        private Dictionary<string, Gimbal> ExtractGimbals(ConfigNode cfg)
        {
            Gimbal ExtractGimbalKeys(ConfigNode c)
            {
                float.TryParse(c.GetValue("gimbalRange"), out float range);
                float xp = 0, xn = 0, yp = 0, yn = 0;
                if (!c.TryGetValue("gimbalRangeXP", ref xp))
                    xp = range;
                if (!c.TryGetValue("gimbalRangeXN", ref xn))
                    xn = range;
                if (!c.TryGetValue("gimbalRangeYP", ref yp))
                    yp = range;
                if (!c.TryGetValue("gimbalRangeYN", ref yn))
                    yn = range;
                return new Gimbal(range, xp, xn, yp, yn);
            }

            var gimbals = new Dictionary<string, Gimbal>();

            if (cfg.HasNode("GIMBAL"))
            {
                foreach (var node in cfg.GetNodes("GIMBAL"))
                {
                    if (!node.HasValue("gimbalTransform"))
                    {
                        Debug.LogError($"*RFMEC* Config {cfg.GetValue("name")} of part {part.name} has a `GIMBAL` node without a `gimbalTransform`!");
                        continue;
                    }
                    gimbals[node.GetValue("gimbalTransform")] = ExtractGimbalKeys(node);
                }
            }
            else if (cfg.HasValue("gimbalRange"))
            {
                var gimbal = ExtractGimbalKeys(cfg);
                if (this.gimbalTransform != string.Empty)
                    gimbals[this.gimbalTransform] = gimbal;
                else
                    foreach (var g in part.Modules.OfType<ModuleGimbal>())
                        gimbals[g.gimbalTransformName] = gimbal;
            }

            return gimbals;
        }

        private void SetGimbalRange(ConfigNode cfg)
        {
            if (!part.HasModuleImplementing<ModuleGimbal>()) return;
            // Do not override gimbals before default gimbals have been extracted.
            if (defaultGimbals == null) return;

            Dictionary<string, Gimbal> gimbalOverrides = ExtractGimbals(cfg);
            foreach (ModuleGimbal mg in part.Modules.OfType<ModuleGimbal>())
            {
                string transform = mg.gimbalTransformName;
                if (!gimbalOverrides.TryGetValue(transform, out Gimbal g))
                {
                    if (!defaultGimbals.ContainsKey(transform))
                    {
                        Debug.LogWarning($"*RFMEC* default gimbal settings were not found for gimbal transform `{transform}` for part {part.name}");
                        continue;
                    }
                    g = defaultGimbals[transform];
                }
                mg.gimbalRange = g.gimbalRange;
                mg.gimbalRangeXP = g.gimbalRangeXP;
                mg.gimbalRangeXN = g.gimbalRangeXN;
                mg.gimbalRangeYP = g.gimbalRangeYP;
                mg.gimbalRangeYN = g.gimbalRangeYN;
            }
        }

        private void HandleEngineIgnitor(ConfigNode cfg)
        {
            // Handle Engine Ignitor
            if (cfg.HasNode("ModuleEngineIgnitor"))
            {
                ConfigNode eiNode = cfg.GetNode("ModuleEngineIgnitor");
                if (part.Modules["ModuleEngineIgnitor"] is PartModule eiPM)
                {
                    if (eiNode.HasValue("ignitionsAvailable") &&
                        int.TryParse(eiNode.GetValue("ignitionsAvailable"), out int ignitions))
                    {
                        ignitions = TechLevels.ConfigIgnitions(ignitions);
                        eiNode.SetValue("ignitionsAvailable", ignitions);
                        eiNode.SetValue("ignitionsRemained", ignitions, true);
                    }
                    if (HighLogic.LoadedSceneIsEditor || (HighLogic.LoadedSceneIsFlight && vessel?.situation == Vessel.Situations.PRELAUNCH)) // fix for prelaunch
                    {
                        int remaining = (int)eiPM.GetType().GetField("ignitionsRemained").GetValue(eiPM);
                        eiNode.SetValue("ignitionsRemained", remaining, true);
                    }
                    ConfigNode tNode = new ConfigNode("MODULE");
                    eiNode.CopyTo(tNode);
                    tNode.SetValue("name", "ModuleEngineIgnitor", true);
                    eiPM.Load(tNode);
                }
                else // backwards compatible with EI nodes when using RF ullage etc.
                {
                    if (eiNode.HasValue("ignitionsAvailable") && !cfg.HasValue("ignitions"))
                        cfg.AddValue("ignitions", eiNode.GetValue("ignitionsAvailable"));
                    if (eiNode.HasValue("useUllageSimulation") && !cfg.HasValue("ullage"))
                        cfg.AddValue("ullage", eiNode.GetValue("useUllageSimulation"));
                    if (eiNode.HasValue("isPressureFed") && !cfg.HasValue("pressureFed"))
                        cfg.AddValue("pressureFed", eiNode.GetValue("isPressureFed"));
                    if (!cfg.HasNode("IGNITOR_RESOURCE"))
                        foreach (ConfigNode resNode in eiNode.GetNodes("IGNITOR_RESOURCE"))
                            cfg.AddNode(resNode);
                }
            }
        }

        #endregion
        virtual public void DoConfig(ConfigNode cfg)
        {
            configMaxThrust = configMinThrust = configHeat = -1f;
            float x = 1;
            if (cfg.TryGetValue(thrustRating, ref x))
                configMaxThrust = scale * x;
            if (cfg.TryGetValue("minThrust", ref x))
                configMinThrust = scale * x;
            if (cfg.TryGetValue("heatProduction", ref x))
                configHeat = (float)Math.Round(x * RFSettings.Instance.heatMultiplier, 0);

            configThrottle = throttle;
            if (cfg.HasValue("throttle"))
                float.TryParse(cfg.GetValue("throttle"), out configThrottle);
            else if (configMinThrust >= 0f && configMaxThrust >= 0f)
                configThrottle = configMinThrust / configMaxThrust;

            float TLMassMult = 1.0f;

            float gimbal = -1f;
            if (cfg.HasValue("gimbalRange"))
                gimbal = float.Parse(cfg.GetValue("gimbalRange"), CultureInfo.InvariantCulture);

            float cost = 0f;
            if (cfg.HasValue("cost"))
                cost = scale * float.Parse(cfg.GetValue("cost"), CultureInfo.InvariantCulture);

            if (techLevel != -1)
            {
                // load techlevels
                TechLevel cTL = new TechLevel();
                cTL.Load(cfg, techNodes, engineType, techLevel);
                TechLevel oTL = new TechLevel();
                oTL.Load(cfg, techNodes, engineType, origTechLevel);

                // set atmosphereCurve
                if (cfg.HasValue("IspSL") && cfg.HasValue("IspV"))
                {
                    cfg.RemoveNode("atmosphereCurve");

                    ConfigNode curve = new ConfigNode("atmosphereCurve");

                    // get the multipliers
                    float.TryParse(cfg.GetValue("IspSL"), out float ispSL);
                    float.TryParse(cfg.GetValue("IspV"), out float ispV);

                    // Mod the curve by the multipliers
                    FloatCurve newAtmoCurve = Utilities.Mod(cTL.AtmosphereCurve, ispSL, ispV);
                    newAtmoCurve.Save(curve);

                    cfg.AddNode(curve);
                }

                // set heatProduction
                if (configHeat > 0)
                    configHeat = TechLevels.MassTL(configHeat);

                // set thrust and throttle
                if (configMaxThrust >= 0)
                {
                    configMaxThrust = TechLevels.ThrustTL(configMaxThrust);
                    if (configMinThrust >= 0)
                        configMinThrust = TechLevels.ThrustTL(configMinThrust);
                    else if (thrustRating.Equals("thrusterPower"))
                        configMinThrust = configMaxThrust * 0.5f;
                    else
                    {
                        configMinThrust = configMaxThrust;
                        if (configThrottle > 1.0f)
                            configThrottle = techLevel >= configThrottle ? 1 : -1;
                        if (configThrottle >= 0.0f)
                        {
                            configThrottle = (float)(configThrottle * cTL.Throttle());
                            configMinThrust *= configThrottle;
                        }
                    }
                    configThrottle = configMinThrust / configMaxThrust;
                    if (origMass > 0)
                        TLMassMult = TechLevels.MassTL(1.0f);
                }
                // Don't want to change gimbals on TL-enabled engines willy-nilly
                // So we don't unless either a transform is specified, or we override.
                // We assume if it was specified in the CONFIG that we should use it anyway.
                if (gimbal < 0 && (!gimbalTransform.Equals(string.Empty) || useGimbalAnyway))
                    gimbal = cTL.GimbalRange;
                if (gimbal >= 0)
                {
                    // allow local override of gimbal mult
                    if (cfg.HasValue("gimbalMult"))
                        gimbal *= float.Parse(cfg.GetValue("gimbalMult"), CultureInfo.InvariantCulture);
                }

                // Cost (multiplier will be 1.0 if unspecified)
                cost = scale * TechLevels.CostTL(cost, cfg);
            }
            else
            {
                if (cfg.HasValue(thrustRating) && configThrottle > 0f && !cfg.HasValue("minThrust"))
                {
                    configMinThrust = configThrottle * configMaxThrust;
                }
            }

            // Now update the cfg from what we did.
            // thrust updates
            // These previously used the format "0.0000" but that sets thrust to 0 for engines with < that in kN
            // so we'll just use default.
            if (configMaxThrust >= 0f)
                cfg.SetValue(thrustRating, configMaxThrust, true);
            if (configMinThrust >= 0f)
                cfg.SetValue("minThrust", configMinThrust, true); // will be ignored by RCS, so what.

            // heat update
            if (configHeat >= 0f)
                cfg.SetValue("heatProduction", configHeat.ToString("0"), true);

            // mass change
            if (origMass > 0)
            {
                configMassMult = scale;
                if (cfg.HasValue("massMult"))
                    if (float.TryParse(cfg.GetValue("massMult"), out float ftmp))
                        configMassMult *= ftmp;

                part.mass = origMass * configMassMult * RFSettings.Instance.EngineMassMultiplier * TLMassMult;
                massDelta = (part.partInfo?.partPrefab is Part p) ? part.mass - p.mass : 0;
            }

            // KIDS integration
            if (cfg.HasNode("atmosphereCurve"))
            {
                ConfigNode newCurveNode = new ConfigNode("atmosphereCurve");
                FloatCurve oldCurve = new FloatCurve();
                oldCurve.Load(cfg.GetNode("atmosphereCurve"));
                FloatCurve newCurve = Utilities.Mod(oldCurve, ispSLMult, ispVMult);
                newCurve.Save(newCurveNode);
                cfg.RemoveNode("atmosphereCurve");
                cfg.AddNode(newCurveNode);
            }
            // gimbal change
            if (gimbal >= 0 && !cfg.HasValue("gimbalRange")) // if TL set a gimbal
                cfg.AddValue("gimbalRange", $"{gimbal * gimbalMult:N4}");
            if (cost != 0f)
                cfg.SetValue("cost", $"{cost:N3}", true);
        }

        //called by StretchyTanks StretchySRB and ProcedrualParts
        virtual public void ChangeThrust(float newThrust)
        {
            foreach (ConfigNode c in configs)
            {
                c.SetValue("maxThrust", newThrust);
            }
            SetConfiguration(configuration);
        }

        // Used by ProceduralParts
        public void ChangeEngineType(string newEngineType)
        {
            engineType = newEngineType;
            SetConfiguration(configuration);
        }

        #endregion

        #region GUI
        public virtual string GUIButtonName => Localizer.GetStringByTag("#RF_Engine_ButtonName"); // "Engine"
        public virtual string EditorDescription => Localizer.GetStringByTag("#RF_Engine_ButtonName_desc"); // "Select a configuration for this engine."
        [KSPField(guiActiveEditor = true, guiName = "#RF_Engine_ButtonName", groupName = groupName), // Engine
         UI_Toggle(enabledText = "#RF_Engine_GUIHide", disabledText = "#RF_Engine_GUIShow")] // Hide GUIShow GUI
        [NonSerialized]
        public bool showRFGUI;

        private void OnPartActionGuiDismiss(Part p)
        {
            if (p == part || p.isSymmetryCounterPart(part))
                showRFGUI = false;
        }

        private void OnPartActionUIShown(UIPartActionWindow window, Part p)
        {
            if (p == part)
                showRFGUI = isMaster;
        }

        public override void OnInactive()
        {
            if (!compatible)
                return;
            if (HighLogic.LoadedSceneIsEditor)
            {
                GameEvents.onPartActionUIDismiss.Remove(OnPartActionGuiDismiss);
                GameEvents.onPartActionUIShown.Remove(OnPartActionUIShown);
            }
        }

        public void OnDestroy()
        {
            GameEvents.onPartActionUIDismiss.Remove(OnPartActionGuiDismiss);
            GameEvents.onPartActionUIShown.Remove(OnPartActionUIShown);

            // Note: We don't call EngineConfigTextures.Cleanup() here because textures
            // are shared across all instances. They'll be cleaned up when Unity unloads the scene.
        }

        private static Vector3 mousePos = Vector3.zero;
        private static Rect guiWindowRect = new Rect(0, 0, 0, 0);
        private static uint lastPartId = 0;
        private static int lastConfigCount = 0;
        private static bool lastCompactView = false;
        private static bool lastHasChart = false;
        private string myToolTip = string.Empty;
        private int counterTT;
        private bool editorLocked = false;

        private Vector2 configScrollPos = Vector2.zero;
        private GUIContent configGuiContent;
        private bool compactView = false;
        private bool useLogScaleX = false; // Toggle for logarithmic x-axis on failure chart
        private bool useLogScaleY = false; // Toggle for logarithmic y-axis on failure chart

        // Column visibility customization
        private bool showColumnMenu = false;
        private static Rect columnMenuRect = new Rect(100, 100, 280, 650); // Separate window rect - tall enough for all columns
        private static bool[] columnsVisibleFull = new bool[18];
        private static bool[] columnsVisibleCompact = new bool[18];
        private static bool columnVisibilityInitialized = false;

        // Simulation controls for data percentage and cluster size
        private bool useSimulatedData = false; // Whether to override real TestFlight data
        private float simulatedDataValue = 0f; // Simulated data value in du (data units)
        private int clusterSize = 1; // Number of engines in cluster (default 1)
        private string clusterSizeInput = "1"; // Text input for cluster size
        private string dataValueInput = "0"; // Text input for data value in du

        private const int ConfigRowHeight = 22;
        private const int ConfigMaxVisibleRows = 16; // Max rows before scrolling (60% taller)
        // Dynamic column widths - calculated based on content
        private float[] ConfigColumnWidths = new float[18];

        // Texture and style management - using singletons to prevent memory leaks
        private EngineConfigTextures Textures => EngineConfigTextures.Instance;

        // Tech level management - lazy initialization
        private EngineConfigTechLevels _techLevels;
        protected EngineConfigTechLevels TechLevels
        {
            get
            {
                if (_techLevels == null)
                    _techLevels = new EngineConfigTechLevels(this);
                return _techLevels;
            }
        }

        // Integration with B9PartSwitch and TestFlight - lazy initialization
        private EngineConfigIntegrations _integrations;
        internal EngineConfigIntegrations Integrations
        {
            get
            {
                if (_integrations == null)
                    _integrations = new EngineConfigIntegrations(this);
                return _integrations;
            }
        }

        // Chart rendering - lazy initialization
        private EngineConfigChart _chart;
        private EngineConfigChart Chart
        {
            get
            {
                if (_chart == null)
                {
                    _chart = new EngineConfigChart(this);
                    _chart.UseLogScaleX = useLogScaleX;
                    _chart.UseLogScaleY = useLogScaleY;
                    _chart.UseSimulatedData = useSimulatedData;
                    _chart.SimulatedDataValue = simulatedDataValue;
                    _chart.ClusterSize = clusterSize;
                }
                return _chart;
            }
        }

        private int toolTipWidth => EditorLogic.fetch.editorScreen == EditorScreen.Parts ? 220 : 300;
        private int toolTipHeight => (int)Styles.styleEditorTooltip.CalcHeight(new GUIContent(myToolTip), toolTipWidth);

        public void OnGUI()
        {
            if (!compatible || !isMaster || !HighLogic.LoadedSceneIsEditor || EditorLogic.fetch == null)
                return;

            bool inPartsEditor = EditorLogic.fetch.editorScreen == EditorScreen.Parts;
            if (!(showRFGUI && inPartsEditor) && !(EditorLogic.fetch.editorScreen == EditorScreen.Actions && EditorActionGroups.Instance.GetSelectedParts().Contains(part)))
            {
                EditorUnlock();
                return;
            }

            if (inPartsEditor && part.symmetryCounterparts.FirstOrDefault(p => p.persistentId < part.persistentId) is Part)
                return;

            if (guiWindowRect.width == 0)
            {
                int posAdd = inPartsEditor ? 256 : 0;
                int posMult = (offsetGUIPos == -1) ? (part.Modules.Contains("ModuleFuelTanks") ? 1 : 0) : offsetGUIPos;
                // Set position with minimal initial size - both width and height will auto-size tightly
                guiWindowRect = new Rect(posAdd + 430 * posMult, 365, 100, 100);
            }

            // Only reset height when switching parts or when content changes (compact view, config count, chart visibility)
            // This prevents flickering during dragging and slider interaction
            uint currentPartId = part.persistentId;
            int currentConfigCount = FilteredDisplayConfigs(false).Count;
            bool currentHasChart = config != null && config.HasValue("cycleReliabilityStart");
            bool contentChanged = currentPartId != lastPartId
                               || currentConfigCount != lastConfigCount
                               || compactView != lastCompactView
                               || currentHasChart != lastHasChart;

            if (contentChanged)
            {
                float savedX = guiWindowRect.x;
                float savedY = guiWindowRect.y;
                float savedWidth = guiWindowRect.width;
                guiWindowRect = new Rect(savedX, savedY, savedWidth, 100);

                lastPartId = currentPartId;
                lastConfigCount = currentConfigCount;
                lastCompactView = compactView;
                lastHasChart = currentHasChart;
            }

            mousePos = Input.mousePosition; //Mouse location; based on Kerbal Engineer Redux code
            mousePos.y = Screen.height - mousePos.y;
            if (guiWindowRect.Contains(mousePos))
                EditorLock();
            else
                EditorUnlock();

            myToolTip = myToolTip.Trim();
            if (!string.IsNullOrEmpty(myToolTip))
            {
                int offset = inPartsEditor ? -222 : 440;
                GUI.Label(new Rect(guiWindowRect.xMin + offset, mousePos.y - 5, toolTipWidth, toolTipHeight), myToolTip, Styles.styleEditorTooltip);
            }

            guiWindowRect = GUILayout.Window(unchecked((int)part.persistentId), guiWindowRect, EngineManagerGUI, Localizer.Format("#RF_Engine_WindowTitle", part.partInfo.title), Styles.styleEditorPanel); // "Configure " + part.partInfo.title

            // Draw column menu as separate window if open
            if (showColumnMenu)
            {
                columnMenuRect = GUI.Window(unchecked((int)part.persistentId) + 1, columnMenuRect, DrawColumnMenuWindow, "Settings", HighLogic.Skin.window);
            }
        }

        private void DrawColumnMenuWindow(int windowID)
        {
            DrawColumnMenu(new Rect(0, 20, columnMenuRect.width, columnMenuRect.height - 20));
            GUI.DragWindow(new Rect(0, 0, columnMenuRect.width, 20));
        }

        private void EditorLock()
        {
            if (!editorLocked)
            {
                EditorLogic.fetch.Lock(false, false, false, "RFGUILock");
                editorLocked = true;
                KSP.UI.Screens.Editor.PartListTooltipMasterController.Instance?.HideTooltip();
            }
        }

        private void EditorUnlock()
        {
            if (editorLocked)
            {
                EditorLogic.fetch.Unlock("RFGUILock");
                editorLocked = false;
            }
        }

        protected string GetCostString(ConfigNode node)
        {
            string costString = string.Empty;
            if (node.HasValue("cost"))
            {
                float curCost = scale * float.Parse(node.GetValue("cost"), CultureInfo.InvariantCulture);

                if (techLevel != -1)
                {
                    curCost = TechLevels.CostTL(curCost, node) - TechLevels.CostTL(0f, node); // get purely the config cost difference
                }
                costString = $" ({((curCost < 0) ? string.Empty : "+")}{curCost:N0}√)";
            }
            return costString;
        }

        /// Normal apply action for the 'select <config>' button.
        protected void GUIApplyConfig(string configName)
        {
            SetConfiguration(configName, true);
            UpdateSymmetryCounterparts();
            GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
            MarkWindowDirty();
        }

        protected void DrawSelectButton(ConfigNode node, bool isSelected, Action<string> apply)
        {
            var nName = node.GetValue("name");
            var dispName = GetConfigDisplayName(node);
            var costString = GetCostString(node);
            var configInfo = GetConfigInfo(node);

            using (new GUILayout.HorizontalScope())
            {
                // For simulations, RP-1 will allow selecting all configs despite tech status or whether the entry cost has been paid.
                // The KCT that comes with RP-1 will call the Validate() method when the player tries to add a vessel to the build queue.
                if (Utilities.RP1Found)
                {
                    // Currently selected.
                    if (isSelected)
                    {
                        GUILayout.Label(new GUIContent($"{Localizer.GetStringByTag("#RF_Engine_CurrentConfig")}: {dispName}{costString}", configInfo)); // Current config
                    }
                    else if (GUILayout.Button(new GUIContent($"{Localizer.GetStringByTag("#RF_Engine_ConfigSwitch")} {dispName}{costString}", configInfo))) // Switch to
                        apply(nName);

                    if (!EngineConfigTechLevels.UnlockedConfig(node, part))
                    {
                        double upgradeCost = EntryCostManager.Instance.ConfigEntryCost(nName);
                        string techRequired = node.GetValue("techRequired");
                        if (upgradeCost <= 0)
                        {
                            // Auto-buy.
                            EntryCostManager.Instance.PurchaseConfig(nName, techRequired);
                        }

                        bool isConfigAvailable = EngineConfigTechLevels.CanConfig(node);
                        string tooltip = string.Empty;
                        if (!isConfigAvailable && techNameToTitle.TryGetValue(techRequired, out string techStr))
                        {
                            tooltip = Localizer.Format("#RF_Engine_LacksTech", techStr); // $"Lacks tech for {techStr}"
                        }

                        GUI.enabled = isConfigAvailable;
                        if (GUILayout.Button(new GUIContent($"{Localizer.GetStringByTag("#RF_Engine_Purchase")} ({upgradeCost:N0}√)", tooltip), GUILayout.Width(145))) // Purchase
                        {
                            if (EntryCostManager.Instance.PurchaseConfig(nName, node.GetValue("techRequired")))
                                apply(nName);
                        }
                        GUI.enabled = true;
                    }
                }
                else
                {
                    // Currently selected.
                    if (isSelected)
                    {
                        GUILayout.Label(new GUIContent($"{Localizer.GetStringByTag("#RF_Engine_CurrentConfig")}: {dispName}{costString}", configInfo)); // Current config
                        return;
                    }

                    // Locked.
                    if (!EngineConfigTechLevels.CanConfig(node))
                    {
                        if (techNameToTitle.TryGetValue(node.GetValue("techRequired"), out string techStr))
                            techStr = $"\n{Localizer.GetStringByTag("#RF_Engine_Requires")}: " + techStr; // Requires
                        GUILayout.Label(new GUIContent(Localizer.Format("#RF_Engine_LacksTech", dispName), configInfo + techStr)); // $"Lacks tech for {dispName}"
                        return;
                    }

                    // Available.
                    if (EngineConfigTechLevels.UnlockedConfig(node, part))
                    {
                        if (GUILayout.Button(new GUIContent($"{Localizer.GetStringByTag("#RF_Engine_ConfigSwitch")} {dispName}{costString}", configInfo))) // Switch to
                            apply(nName);
                        return;
                    }

                    // Purchase.
                    double upgradeCost = EntryCostManager.Instance.ConfigEntryCost(nName);
                    string techRequired = node.GetValue("techRequired");
                    if (upgradeCost > 0d)
                    {
                        costString = $" ({upgradeCost:N0}√)";
                        if (GUILayout.Button(new GUIContent($"{Localizer.GetStringByTag("#RF_Engine_Purchase")}  {dispName}{costString}", configInfo))) // Purchase
                        {
                            if (EntryCostManager.Instance.PurchaseConfig(nName, techRequired))
                                apply(nName);
                        }
                    }
                    else
                    {
                        // Auto-buy.
                        EntryCostManager.Instance.PurchaseConfig(nName, techRequired);
                        if (GUILayout.Button(new GUIContent($"{Localizer.GetStringByTag("#RF_Engine_ConfigSwitch")}  {dispName}{costString}", configInfo))) // Switch to
                            apply(nName);
                    }
                }
            }
        }

        protected struct ConfigRowDefinition
        {
            public ConfigNode Node;
            public string DisplayName;
            public bool IsSelected;
            public bool Indent;
            public Action Apply;
        }

        protected virtual IEnumerable<ConfigRowDefinition> BuildConfigRows()
        {
            foreach (ConfigNode node in FilteredDisplayConfigs(false))
            {
                string configName = node.GetValue("name");
                yield return new ConfigRowDefinition
                {
                    Node = node,
                    DisplayName = GetConfigDisplayName(node),
                    IsSelected = configName == configuration,
                    Indent = false,
                    Apply = () => GUIApplyConfig(configName)
                };
            }
        }

        protected void CalculateColumnWidths(List<ConfigRowDefinition> rows)
        {
            // Create style for measuring cell content
            var cellStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(5, 0, 0, 0)
            };

            // Initialize with minimum widths
            for (int i = 0; i < ConfigColumnWidths.Length; i++)
            {
                ConfigColumnWidths[i] = 30f; // Start with minimum
            }

            // Measure all row content (ignore headers - they're rotated and centered)
            foreach (var row in rows)
            {
                string nameText = row.DisplayName;
                if (row.Indent) nameText = "    ↳ " + nameText;

                string[] cellValues = new string[]
                {
                    nameText,
                    GetThrustString(row.Node),
                    GetMinThrottleString(row.Node),
                    GetIspString(row.Node),
                    GetMassString(row.Node),
                    GetGimbalString(row.Node),
                    GetIgnitionsString(row.Node),
                    GetBoolSymbol(row.Node, "ullage"),
                    GetBoolSymbol(row.Node, "pressureFed"),
                    GetRatedBurnTimeString(row.Node),
                    GetTestedBurnTimeString(row.Node), // NEW: Tested burn time column
                    GetIgnitionReliabilityStartString(row.Node),
                    GetIgnitionReliabilityEndString(row.Node),
                    GetCycleReliabilityStartString(row.Node),
                    GetCycleReliabilityEndString(row.Node),
                    GetTechString(row.Node),
                    GetCostDeltaString(row.Node),
                    "" // Action column - buttons
                };

                for (int i = 0; i < cellValues.Length; i++)
                {
                    if (!string.IsNullOrEmpty(cellValues[i]))
                    {
                        float width = cellStyle.CalcSize(new GUIContent(cellValues[i])).x + 10f; // Add padding
                        if (width > ConfigColumnWidths[i])
                            ConfigColumnWidths[i] = width;
                    }
                }
            }

            // Action column needs fixed width for two buttons
            ConfigColumnWidths[17] = 160f;

            // Set minimum widths for specific columns
            ConfigColumnWidths[7] = Mathf.Max(ConfigColumnWidths[7], 30f); // Ull
            ConfigColumnWidths[8] = Mathf.Max(ConfigColumnWidths[8], 30f); // PFed
            ConfigColumnWidths[9] = Mathf.Max(ConfigColumnWidths[9], 50f); // Rated burn
            ConfigColumnWidths[10] = Mathf.Max(ConfigColumnWidths[10], 50f); // Tested burn
        }

        protected void DrawConfigTable(IEnumerable<ConfigRowDefinition> rows)
        {
            EnsureTexturesAndStyles();

            var rowList = rows.ToList();

            // Calculate dynamic column widths
            CalculateColumnWidths(rowList);

            // Sum only visible column widths
            float totalWidth = 0f;
            for (int i = 0; i < ConfigColumnWidths.Length; i++)
            {
                if (IsColumnVisible(i))
                    totalWidth += ConfigColumnWidths[i];
            }

            // Update window width to fit table exactly (accounting for window padding: 5px left + 5px right = 10px)
            float requiredWindowWidth = totalWidth + 10f; // Table width + padding
            const float minWindowWidth = 900f; // Minimum width to prevent squishing
            guiWindowRect.width = Mathf.Max(requiredWindowWidth, minWindowWidth);

            Rect headerRowRect = GUILayoutUtility.GetRect(GUIContent.none, GUI.skin.label, GUILayout.Height(45));
            float headerStartX = headerRowRect.x; // No left margin
            DrawHeaderRow(new Rect(headerStartX, headerRowRect.y, totalWidth, headerRowRect.height));

            // Dynamic height: grow up to max, then scroll
            int actualRows = rowList.Count;
            int visibleRows = Mathf.Min(actualRows, ConfigMaxVisibleRows);
            int scrollViewHeight = visibleRows * ConfigRowHeight;

            // No spacing in scroll view
            var scrollStyle = new GUIStyle(GUI.skin.scrollView) { padding = new RectOffset(0, 0, 0, 0) };
            configScrollPos = GUILayout.BeginScrollView(configScrollPos, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, scrollStyle, GUILayout.Height(scrollViewHeight));

            // Use a style with no margin/padding for tight row spacing
            var noSpaceStyle = new GUIStyle { margin = new RectOffset(0, 0, 0, 0), padding = new RectOffset(0, 0, 0, 0) };

            int rowIndex = 0;
            foreach (var row in rowList)
            {
                Rect rowRect = GUILayoutUtility.GetRect(GUIContent.none, noSpaceStyle, GUILayout.Height(ConfigRowHeight));
                float rowStartX = rowRect.x; // No left margin
                Rect tableRowRect = new Rect(rowStartX, rowRect.y, totalWidth, rowRect.height);
                bool isHovered = tableRowRect.Contains(Event.current.mousePosition);

                bool isLocked = !EngineConfigTechLevels.CanConfig(row.Node);
                if (Event.current.type == EventType.Repaint)
                {
                    // Draw alternating row background first
                    if (!row.IsSelected && !isLocked && !isHovered && rowIndex % 2 == 1)
                    {
                        GUI.DrawTexture(tableRowRect, Textures.ZebraStripe);
                    }

                    if (row.IsSelected)
                        GUI.DrawTexture(tableRowRect, Textures.RowCurrent);
                    else if (isLocked)
                        GUI.DrawTexture(tableRowRect, Textures.RowLocked);
                    else if (isHovered)
                        GUI.DrawTexture(tableRowRect, Textures.RowHover);
                }

                string tooltip = GetRowTooltip(row.Node);
                if (configGuiContent == null)
                    configGuiContent = new GUIContent();
                configGuiContent.text = string.Empty;
                configGuiContent.tooltip = tooltip;
                GUI.Label(tableRowRect, configGuiContent, GUIStyle.none);

                DrawConfigRow(tableRowRect, row, isHovered, isLocked);

                // Draw column separators
                if (Event.current.type == EventType.Repaint)
                {
                    DrawColumnSeparators(tableRowRect);
                }

                rowIndex++;
            }

            GUILayout.EndScrollView();
        }

        private void DrawColumnMenu(Rect menuRect)
        {
            InitializeColumnVisibility();

            // Column names
            string[] columnNames = {
                "Name", "Thrust", "Min%", "ISP", "Mass", "Gimbal",
                "Ignitions", "Ullage", "Press-Fed", "Rated (s)", "Tested (s)",
                "Ign No Data", "Ign Max Data", "Burn No Data", "Burn Max Data",
                "Tech", "Cost", "Actions"
            };

            float yPos = menuRect.y + 10;
            float leftX = menuRect.x + 10;
            float rightX = menuRect.x + menuRect.width / 2 + 5;

            // Use cached menu styles
            GUIStyle headerStyle = EngineConfigStyles.MenuHeader;
            GUIStyle labelStyle = EngineConfigStyles.MenuLabel;

            // Title
            GUI.Label(new Rect(leftX, yPos, menuRect.width - 20, 20), "Column Visibility", headerStyle);
            yPos += 25;

            // Separator
            if (Event.current.type == EventType.Repaint)
            {
                Texture2D separatorTex = Styles.CreateColorPixel(new Color(0.3f, 0.3f, 0.3f, 0.5f));
                GUI.DrawTexture(new Rect(leftX, yPos, menuRect.width - 20, 1), separatorTex);
            }
            yPos += 10;

            // Headers for Full and Compact
            GUI.Label(new Rect(leftX + 100, yPos, 60, 20), "Full", headerStyle);
            GUI.Label(new Rect(leftX + 170, yPos, 60, 20), "Compact", headerStyle);
            yPos += 25;

            // Scrollable area for columns
            Rect scrollRect = new Rect(leftX, yPos, menuRect.width - 20, menuRect.height - 80);
            Rect viewRect = new Rect(0, 0, scrollRect.width - 20, columnNames.Length * 25);

            GUI.BeginGroup(scrollRect);
            float itemY = 0;

            for (int i = 0; i < columnNames.Length; i++)
            {
                // Column name
                GUI.Label(new Rect(5, itemY, 90, 20), columnNames[i], labelStyle);

                // Full view checkbox
                bool newFullVisible = GUI.Toggle(new Rect(105, itemY, 20, 20), columnsVisibleFull[i], "");
                if (newFullVisible != columnsVisibleFull[i])
                {
                    columnsVisibleFull[i] = newFullVisible;
                }

                // Compact view checkbox
                bool newCompactVisible = GUI.Toggle(new Rect(175, itemY, 20, 20), columnsVisibleCompact[i], "");
                if (newCompactVisible != columnsVisibleCompact[i])
                {
                    columnsVisibleCompact[i] = newCompactVisible;
                }

                itemY += 25;
            }

            GUI.EndGroup();

            // Close button
            if (GUI.Button(new Rect(menuRect.x + menuRect.width - 60, menuRect.y + menuRect.height - 30, 50, 20), "Close"))
            {
                showColumnMenu = false;
            }
        }

        private void DrawHeaderRow(Rect headerRect)
        {
            float currentX = headerRect.x;
            if (IsColumnVisible(0)) {
                DrawHeaderCell(new Rect(currentX, headerRect.y, ConfigColumnWidths[0], headerRect.height),
                    "Name", "Configuration name");
                currentX += ConfigColumnWidths[0];
            }
            if (IsColumnVisible(1)) {
                DrawHeaderCell(new Rect(currentX, headerRect.y, ConfigColumnWidths[1], headerRect.height),
                    Localizer.GetStringByTag("#RF_EngineRF_Thrust"), "Rated thrust");
                currentX += ConfigColumnWidths[1];
            }
            if (IsColumnVisible(2)) {
                DrawHeaderCell(new Rect(currentX, headerRect.y, ConfigColumnWidths[2], headerRect.height),
                    "Min%", "Minimum throttle");
                currentX += ConfigColumnWidths[2];
            }
            if (IsColumnVisible(3)) {
                DrawHeaderCell(new Rect(currentX, headerRect.y, ConfigColumnWidths[3], headerRect.height),
                    Localizer.GetStringByTag("#RF_Engine_Isp"), "Sea level and vacuum Isp");
                currentX += ConfigColumnWidths[3];
            }
            if (IsColumnVisible(4)) {
                DrawHeaderCell(new Rect(currentX, headerRect.y, ConfigColumnWidths[4], headerRect.height),
                    Localizer.GetStringByTag("#RF_Engine_Enginemass"), "Engine mass");
                currentX += ConfigColumnWidths[4];
            }
            if (IsColumnVisible(5)) {
                DrawHeaderCell(new Rect(currentX, headerRect.y, ConfigColumnWidths[5], headerRect.height),
                    Localizer.GetStringByTag("#RF_Engine_TLTInfo_Gimbal"), "Gimbal range");
                currentX += ConfigColumnWidths[5];
            }
            if (IsColumnVisible(6)) {
                DrawHeaderCell(new Rect(currentX, headerRect.y, ConfigColumnWidths[6], headerRect.height),
                    Localizer.GetStringByTag("#RF_EngineRF_Ignitions"), "Ignitions");
                currentX += ConfigColumnWidths[6];
            }
            if (IsColumnVisible(7)) {
                DrawHeaderCell(new Rect(currentX, headerRect.y, ConfigColumnWidths[7], headerRect.height),
                    Localizer.GetStringByTag("#RF_Engine_ullage"), "Ullage requirement");
                currentX += ConfigColumnWidths[7];
            }
            if (IsColumnVisible(8)) {
                DrawHeaderCell(new Rect(currentX, headerRect.y, ConfigColumnWidths[8], headerRect.height),
                    Localizer.GetStringByTag("#RF_Engine_pressureFed"), "Pressure-fed");
                currentX += ConfigColumnWidths[8];
            }
            if (IsColumnVisible(9)) {
                DrawHeaderCell(new Rect(currentX, headerRect.y, ConfigColumnWidths[9], headerRect.height),
                    "Rated (s)", "Rated burn time");
                currentX += ConfigColumnWidths[9];
            }
            if (IsColumnVisible(10)) {
                DrawHeaderCell(new Rect(currentX, headerRect.y, ConfigColumnWidths[10], headerRect.height),
                    "Tested (s)", "Tested burn time (real-world test duration)");
                currentX += ConfigColumnWidths[10];
            }
            if (IsColumnVisible(11)) {
                DrawHeaderCell(new Rect(currentX, headerRect.y, ConfigColumnWidths[11], headerRect.height),
                    "Ign No Data", "Ignition reliability at 0 data");
                currentX += ConfigColumnWidths[11];
            }
            if (IsColumnVisible(12)) {
                DrawHeaderCell(new Rect(currentX, headerRect.y, ConfigColumnWidths[12], headerRect.height),
                    "Ign Max Data", "Ignition reliability at max data");
                currentX += ConfigColumnWidths[12];
            }
            if (IsColumnVisible(13)) {
                DrawHeaderCell(new Rect(currentX, headerRect.y, ConfigColumnWidths[13], headerRect.height),
                    "Burn No Data", "Cycle reliability at 0 data");
                currentX += ConfigColumnWidths[13];
            }
            if (IsColumnVisible(14)) {
                DrawHeaderCell(new Rect(currentX, headerRect.y, ConfigColumnWidths[14], headerRect.height),
                    "Burn Max Data", "Cycle reliability at max data");
                currentX += ConfigColumnWidths[14];
            }
            if (IsColumnVisible(15)) {
                DrawHeaderCell(new Rect(currentX, headerRect.y, ConfigColumnWidths[15], headerRect.height),
                    Localizer.GetStringByTag("#RF_Engine_Requires"), "Required technology");
                currentX += ConfigColumnWidths[15];
            }
            if (IsColumnVisible(16)) {
                DrawHeaderCell(new Rect(currentX, headerRect.y, ConfigColumnWidths[16], headerRect.height),
                    "Extra Cost", "Extra cost for this config");
                currentX += ConfigColumnWidths[16];
            }
            if (IsColumnVisible(17)) {
                DrawHeaderCell(new Rect(currentX, headerRect.y, ConfigColumnWidths[17], headerRect.height),
                    "", "Switch and purchase actions"); // No label, just tooltip
            }
        }

        private void DrawColumnSeparators(Rect rowRect)
        {
            float currentX = rowRect.x;
            for (int i = 0; i < ConfigColumnWidths.Length - 1; i++)
            {
                if (IsColumnVisible(i))
                {
                    currentX += ConfigColumnWidths[i];
                    Rect separatorRect = new Rect(currentX, rowRect.y, 1, rowRect.height);
                    GUI.DrawTexture(separatorRect, Textures.ColumnSeparator);
                }
            }
        }

        private void DrawHeaderCell(Rect rect, string text, string tooltip)
        {
            bool hover = rect.Contains(Event.current.mousePosition);
            GUIStyle headerStyle = hover ? EngineConfigStyles.HeaderCellHover : EngineConfigStyles.HeaderCell;

            if (configGuiContent == null)
                configGuiContent = new GUIContent();
            configGuiContent.text = text;
            configGuiContent.tooltip = tooltip;
            Matrix4x4 matrixBackup = GUI.matrix;
            // Start text at horizontal center of column
            float offsetX = rect.width / 2f;
            Vector2 pivot = new Vector2(rect.x + offsetX, rect.y + rect.height + 4f);
            GUIUtility.RotateAroundPivot(-45f, pivot);
            GUI.Label(new Rect(rect.x + offsetX, rect.y + rect.height - 22f, 140f, 24f), configGuiContent, headerStyle);
            GUI.matrix = matrixBackup;
        }

        private void DrawConfigRow(Rect rowRect, ConfigRowDefinition row, bool isHovered, bool isLocked)
        {
            // Use cached styles instead of creating new ones every frame
            GUIStyle primaryStyle;
            if (isLocked)
                primaryStyle = EngineConfigStyles.RowPrimaryLocked;
            else if (isHovered)
                primaryStyle = EngineConfigStyles.RowPrimaryHover;
            else
                primaryStyle = EngineConfigStyles.RowPrimary;

            GUIStyle secondaryStyle = EngineConfigStyles.RowSecondary;

            float currentX = rowRect.x;
            string nameText = row.DisplayName;
            if (row.Indent) nameText = "    ↳ " + nameText;

            if (IsColumnVisible(0)) {
                GUI.Label(new Rect(currentX, rowRect.y, ConfigColumnWidths[0], rowRect.height), nameText, primaryStyle);
                currentX += ConfigColumnWidths[0];
            }

            if (IsColumnVisible(1)) {
                GUI.Label(new Rect(currentX, rowRect.y, ConfigColumnWidths[1], rowRect.height), GetThrustString(row.Node), secondaryStyle);
                currentX += ConfigColumnWidths[1];
            }

            if (IsColumnVisible(2)) {
                GUI.Label(new Rect(currentX, rowRect.y, ConfigColumnWidths[2], rowRect.height), GetMinThrottleString(row.Node), secondaryStyle);
                currentX += ConfigColumnWidths[2];
            }

            if (IsColumnVisible(3)) {
                GUI.Label(new Rect(currentX, rowRect.y, ConfigColumnWidths[3], rowRect.height), GetIspString(row.Node), secondaryStyle);
                currentX += ConfigColumnWidths[3];
            }

            if (IsColumnVisible(4)) {
                GUI.Label(new Rect(currentX, rowRect.y, ConfigColumnWidths[4], rowRect.height), GetMassString(row.Node), secondaryStyle);
                currentX += ConfigColumnWidths[4];
            }

            if (IsColumnVisible(5)) {
                GUI.Label(new Rect(currentX, rowRect.y, ConfigColumnWidths[5], rowRect.height), GetGimbalString(row.Node), secondaryStyle);
                currentX += ConfigColumnWidths[5];
            }

            if (IsColumnVisible(6)) {
                GUI.Label(new Rect(currentX, rowRect.y, ConfigColumnWidths[6], rowRect.height), GetIgnitionsString(row.Node), secondaryStyle);
                currentX += ConfigColumnWidths[6];
            }

            if (IsColumnVisible(7)) {
                GUI.Label(new Rect(currentX, rowRect.y, ConfigColumnWidths[7], rowRect.height), GetBoolSymbol(row.Node, "ullage"), secondaryStyle);
                currentX += ConfigColumnWidths[7];
            }

            if (IsColumnVisible(8)) {
                GUI.Label(new Rect(currentX, rowRect.y, ConfigColumnWidths[8], rowRect.height), GetBoolSymbol(row.Node, "pressureFed"), secondaryStyle);
                currentX += ConfigColumnWidths[8];
            }

            if (IsColumnVisible(9)) {
                GUI.Label(new Rect(currentX, rowRect.y, ConfigColumnWidths[9], rowRect.height), GetRatedBurnTimeString(row.Node), secondaryStyle);
                currentX += ConfigColumnWidths[9];
            }

            if (IsColumnVisible(10)) {
                GUI.Label(new Rect(currentX, rowRect.y, ConfigColumnWidths[10], rowRect.height), GetTestedBurnTimeString(row.Node), secondaryStyle);
                currentX += ConfigColumnWidths[10];
            }

            if (IsColumnVisible(11)) {
                GUI.Label(new Rect(currentX, rowRect.y, ConfigColumnWidths[11], rowRect.height), GetIgnitionReliabilityStartString(row.Node), secondaryStyle);
                currentX += ConfigColumnWidths[11];
            }

            if (IsColumnVisible(12)) {
                GUI.Label(new Rect(currentX, rowRect.y, ConfigColumnWidths[12], rowRect.height), GetIgnitionReliabilityEndString(row.Node), secondaryStyle);
                currentX += ConfigColumnWidths[12];
            }

            if (IsColumnVisible(13)) {
                GUI.Label(new Rect(currentX, rowRect.y, ConfigColumnWidths[13], rowRect.height), GetCycleReliabilityStartString(row.Node), secondaryStyle);
                currentX += ConfigColumnWidths[13];
            }

            if (IsColumnVisible(14)) {
                GUI.Label(new Rect(currentX, rowRect.y, ConfigColumnWidths[14], rowRect.height), GetCycleReliabilityEndString(row.Node), secondaryStyle);
                currentX += ConfigColumnWidths[14];
            }

            if (IsColumnVisible(15)) {
                GUI.Label(new Rect(currentX, rowRect.y, ConfigColumnWidths[15], rowRect.height), GetTechString(row.Node), secondaryStyle);
                currentX += ConfigColumnWidths[15];
            }

            if (IsColumnVisible(16)) {
                GUI.Label(new Rect(currentX, rowRect.y, ConfigColumnWidths[16], rowRect.height), GetCostDeltaString(row.Node), secondaryStyle);
                currentX += ConfigColumnWidths[16];
            }

            if (IsColumnVisible(17)) {
                DrawActionCell(new Rect(currentX, rowRect.y + 1, ConfigColumnWidths[17], rowRect.height - 2), row.Node, row.IsSelected, row.Apply);
            }
        }

        private void DrawActionCell(Rect rect, ConfigNode node, bool isSelected, Action apply)
        {
            GUIStyle smallButtonStyle = EngineConfigStyles.SmallButton;

            string configName = node.GetValue("name");
            bool canUse = EngineConfigTechLevels.CanConfig(node);
            bool unlocked = EngineConfigTechLevels.UnlockedConfig(node, part);
            double cost = EntryCostManager.Instance.ConfigEntryCost(configName);

            // Auto-purchase free configs
            if (cost <= 0 && !unlocked && canUse)
                EntryCostManager.Instance.PurchaseConfig(configName, node.GetValue("techRequired"));

            // Split the rect into two buttons side by side
            float buttonWidth = rect.width / 2f - 2f;
            Rect switchRect = new Rect(rect.x, rect.y, buttonWidth, rect.height);
            Rect purchaseRect = new Rect(rect.x + buttonWidth + 4f, rect.y, buttonWidth, rect.height);

            // Switch button - always enabled except when already selected
            GUI.enabled = !isSelected;
            string switchLabel = isSelected ? "Active" : "Switch";
            if (GUI.Button(switchRect, switchLabel, smallButtonStyle))
            {
                if (!unlocked && cost <= 0)
                    EntryCostManager.Instance.PurchaseConfig(configName, node.GetValue("techRequired"));
                apply?.Invoke();
            }

            // Purchase button (shows cost)
            GUI.enabled = canUse && !unlocked && cost > 0;
            string purchaseLabel;
            if (cost > 0)
                purchaseLabel = unlocked ? "Owned" : $"Buy ({cost:N0}√)";
            else
                purchaseLabel = "Free";

            if (GUI.Button(purchaseRect, purchaseLabel, smallButtonStyle))
            {
                if (EntryCostManager.Instance.PurchaseConfig(configName, node.GetValue("techRequired")))
                    apply?.Invoke();
            }

            GUI.enabled = true;
        }

        internal string GetThrustString(ConfigNode node)
        {
            if (!node.HasValue(thrustRating))
                return "-";

            float thrust = scale * TechLevels.ThrustTL(node.GetValue(thrustRating), node);
            // Remove decimals for large thrust values
            if (thrust >= 100f)
                return $"{thrust:N0} kN";
            return $"{thrust:N2} kN";
        }

        internal string GetMinThrottleString(ConfigNode node)
        {
            float value = -1f;
            if (node.HasValue("minThrust") && node.HasValue(thrustRating))
            {
                float.TryParse(node.GetValue("minThrust"), out float minT);
                float.TryParse(node.GetValue(thrustRating), out float maxT);
                if (maxT > 0)
                    value = minT / maxT;
            }
            else if (node.HasValue("throttle"))
            {
                float.TryParse(node.GetValue("throttle"), out value);
            }

            if (value < 0f)
                return "-";
            return value.ToString("P0");
        }

        internal string GetIspString(ConfigNode node)
        {
            if (node.HasNode("atmosphereCurve"))
            {
                FloatCurve isp = new FloatCurve();
                isp.Load(node.GetNode("atmosphereCurve"));
                float ispVac = isp.Evaluate(isp.maxTime);
                float ispSL = isp.Evaluate(isp.minTime);
                return $"{ispVac:N0}-{ispSL:N0}";
            }

            if (node.HasValue("IspSL") && node.HasValue("IspV"))
            {
                float.TryParse(node.GetValue("IspSL"), out float ispSL);
                float.TryParse(node.GetValue("IspV"), out float ispV);
                if (techLevel != -1)
                {
                    TechLevel cTL = new TechLevel();
                    if (cTL.Load(node, techNodes, engineType, techLevel))
                    {
                        ispSL *= ispSLMult * cTL.AtmosphereCurve.Evaluate(1);
                        ispV *= ispVMult * cTL.AtmosphereCurve.Evaluate(0);
                    }
                }
                return $"{ispV:N0}-{ispSL:N0}";
            }

            return "-";
        }

        internal string GetMassString(ConfigNode node)
        {
            if (origMass <= 0f)
                return "-";

            float cMass = scale * origMass * RFSettings.Instance.EngineMassMultiplier;
            if (node.HasValue("massMult") && float.TryParse(node.GetValue("massMult"), out float ftmp))
                cMass *= ftmp;

            return $"{cMass:N3}t";
        }

        internal string GetGimbalString(ConfigNode node)
        {
            if (!part.HasModuleImplementing<ModuleGimbal>())
                return "<color=#9E9E9E>✗</color>";

            var gimbals = ExtractGimbals(node);

            // If no explicit gimbal in config, check if we should use tech level gimbal
            if (gimbals.Count == 0 && techLevel != -1 && (!gimbalTransform.Equals(string.Empty) || useGimbalAnyway))
            {
                TechLevel cTL = new TechLevel();
                if (cTL.Load(node, techNodes, engineType, techLevel))
                {
                    float gimbalRange = cTL.GimbalRange;
                    if (node.HasValue("gimbalMult"))
                        gimbalRange *= float.Parse(node.GetValue("gimbalMult"), CultureInfo.InvariantCulture);

                    if (gimbalRange >= 0)
                        return $"{gimbalRange * gimbalMult:0.#}°";
                }
            }

            // Fallback: if config has no gimbal data, use the part's ModuleGimbal
            if (gimbals.Count == 0)
            {
                foreach (var gimbalMod in part.Modules.OfType<ModuleGimbal>())
                {
                    if (gimbalMod != null)
                    {
                        var gimbal = new Gimbal(gimbalMod.gimbalRange, gimbalMod.gimbalRange, gimbalMod.gimbalRange, gimbalMod.gimbalRange, gimbalMod.gimbalRange);
                        gimbals[gimbalMod.gimbalTransformName] = gimbal;
                    }
                }
            }

            if (gimbals.Count == 0)
                return "<color=#9E9E9E>✗</color>";

            var first = gimbals.Values.First();
            bool allSame = gimbals.Values.All(g => g.gimbalRange == first.gimbalRange
                && g.gimbalRangeXP == first.gimbalRangeXP
                && g.gimbalRangeXN == first.gimbalRangeXN
                && g.gimbalRangeYP == first.gimbalRangeYP
                && g.gimbalRangeYN == first.gimbalRangeYN);

            if (allSame)
                return first.Info();

            // Multiple different gimbal ranges - list them all
            var uniqueInfos = gimbals.Values.Select(g => g.Info()).Distinct().OrderBy(s => s);
            return string.Join(", ", uniqueInfos);
        }

        internal string GetIgnitionsString(ConfigNode node)
        {
            if (!node.HasValue("ignitions"))
                return "-";

            if (!int.TryParse(node.GetValue("ignitions"), out int ignitions))
                return "∞";

            int resolved = TechLevels.ConfigIgnitions(ignitions);
            if (resolved == -1)
                return "∞";
            if (resolved == 0 && literalZeroIgnitions)
                return "<color=#FFEB3B>Gnd</color>"; // Yellow G for ground-only ignitions
            return resolved.ToString();
        }

        internal string GetBoolSymbol(ConfigNode node, string key)
        {
            if (!node.HasValue(key))
                return "<color=#9E9E9E>✗</color>"; // Treat missing as false - gray (no restriction)
            bool isTrue = node.GetValue(key).ToLower() == "true";
            return isTrue ? "<color=#FFA726>✓</color>" : "<color=#9E9E9E>✗</color>"; // Orange for restriction, gray for no restriction
        }

        private void InitializeColumnVisibility()
        {
            if (columnVisibilityInitialized)
                return;

            // Initialize full view: all columns visible by default
            for (int i = 0; i < 18; i++)
                columnsVisibleFull[i] = true;

            // Initialize compact view: only essential columns
            for (int i = 0; i < 18; i++)
                columnsVisibleCompact[i] = false;

            // Essential columns for compact view
            int[] compactColumns = { 0, 1, 3, 4, 6, 9, 10, 15, 16, 17 }; // Tech, Cost, Actions
            foreach (int col in compactColumns)
                columnsVisibleCompact[col] = true;

            columnVisibilityInitialized = true;
        }

        private bool IsColumnVisible(int columnIndex)
        {
            InitializeColumnVisibility();

            if (columnIndex < 0 || columnIndex >= 18)
                return false;

            return compactView ? columnsVisibleCompact[columnIndex] : columnsVisibleFull[columnIndex];
        }

        internal string GetRatedBurnTimeString(ConfigNode node)
        {
            bool hasRatedBurnTime = node.HasValue("ratedBurnTime");
            bool hasRatedContinuousBurnTime = node.HasValue("ratedContinuousBurnTime");

            if (!hasRatedBurnTime && !hasRatedContinuousBurnTime)
                return "∞";

            // If both values exist, show as "continuous/cumulative"
            if (hasRatedBurnTime && hasRatedContinuousBurnTime)
            {
                string continuous = node.GetValue("ratedContinuousBurnTime");
                string cumulative = node.GetValue("ratedBurnTime");
                return $"{continuous}/{cumulative}";
            }

            // Otherwise show whichever one exists
            return hasRatedBurnTime ? node.GetValue("ratedBurnTime") : node.GetValue("ratedContinuousBurnTime");
        }

        internal string GetTestedBurnTimeString(ConfigNode node)
        {
            // Values are copied to CONFIG level by ModuleManager patch
            if (!node.HasValue("testedBurnTime"))
                return "-";

            float testedBurnTime = 0f;
            if (node.TryGetValue("testedBurnTime", ref testedBurnTime))
                return testedBurnTime.ToString("F0");

            return "-";
        }

        internal string GetIgnitionReliabilityStartString(ConfigNode node)
        {
            // Values are copied to CONFIG level by ModuleManager patch
            if (!node.HasValue("ignitionReliabilityStart"))
                return "-";
            if (float.TryParse(node.GetValue("ignitionReliabilityStart"), out float val))
                return $"{val:P1}";
            return "-";
        }

        internal string GetIgnitionReliabilityEndString(ConfigNode node)
        {
            // Values are copied to CONFIG level by ModuleManager patch
            if (!node.HasValue("ignitionReliabilityEnd"))
                return "-";
            if (float.TryParse(node.GetValue("ignitionReliabilityEnd"), out float val))
                return $"{val:P1}";
            return "-";
        }

        internal string GetCycleReliabilityStartString(ConfigNode node)
        {
            // Values are copied to CONFIG level by ModuleManager patch
            if (!node.HasValue("cycleReliabilityStart"))
                return "-";
            if (float.TryParse(node.GetValue("cycleReliabilityStart"), out float val))
                return $"{val:P1}";
            return "-";
        }

        internal string GetCycleReliabilityEndString(ConfigNode node)
        {
            // Values are copied to CONFIG level by ModuleManager patch
            if (!node.HasValue("cycleReliabilityEnd"))
                return "-";
            if (float.TryParse(node.GetValue("cycleReliabilityEnd"), out float val))
                return $"{val:P1}";
            return "-";
        }

        private string GetFlightDataString()
        {
            // Get current flight data from TestFlight
            float currentData = TestFlightWrapper.GetCurrentFlightData(part);
            float maxData = TestFlightWrapper.GetMaximumData(part);

            if (currentData < 0f || maxData <= 0f)
                return "-";

            return $"{currentData:F0} / {maxData:F0}";
        }

        internal string GetTechString(ConfigNode node)
        {
            if (!node.HasValue("techRequired"))
                return "-";

            string tech = node.GetValue("techRequired");
            if (techNameToTitle.TryGetValue(tech, out string title))
                tech = title;

            // Abbreviate: keep first word, then first 4 letters of other words with "-"
            var words = tech.Split(' ');
            if (words.Length <= 1)
                return tech;

            var abbreviated = words[0];
            for (int i = 1; i < words.Length; i++)
            {
                if (words[i].Length > 4)
                    abbreviated += "-" + words[i].Substring(0, 4);
                else
                    abbreviated += "-" + words[i];
            }
            return abbreviated;
        }

        internal string GetCostDeltaString(ConfigNode node)
        {
            if (!node.HasValue("cost"))
                return "-";

            float curCost = scale * float.Parse(node.GetValue("cost"), CultureInfo.InvariantCulture);
            if (techLevel != -1)
                curCost = TechLevels.CostTL(curCost, node) - TechLevels.CostTL(0f, node);

            if (Mathf.Approximately(curCost, 0f))
                return "-";

            string sign = curCost < 0 ? string.Empty : "+";
            return $"{sign}{curCost:N0}√";
        }

        #region Removed TestFlight UI Integration
        // All TestFlight column display code removed due to:
        // 1. Data spread across multiple modules (TestFlightCore, TestFlightFailure_IgnitionFail)
        // 2. Reliability/MTBF values require complex calculations, not simple field access
        // 3. Reflection-based approach was error-prone and caused GUI crashes
        //
        // TestFlight integration still works via UpdateTFInterops() to notify TestFlight
        // of active configuration changes. TestFlight UI displays its own data.
        //
        // Removed methods:
        // - GetFlightDataString, GetIgnitionChanceString, GetIgnitionChanceAtMaxDataString
        // - GetReliabilityString, GetReliabilityAtMaxDataString
        // - TryGetTestFlightStats, GetAllTestFlightDataSources, GetTestFlightDataSource
        // - TryGetConfigDataSource, TryGetNumber, TryGetMemberValue, TryGetStringMember
        // - TryConvertToDouble, FormatPercent
        // - TestFlightStats struct
        #endregion

        private string GetRowTooltip(ConfigNode node)
        {
            List<string> tooltipParts = new List<string>();

            // Add description if present
            if (node.HasValue("description"))
                tooltipParts.Add(node.GetValue("description"));

            // Add propellants with flow rates if present
            if (node.HasNode("PROPELLANT"))
            {
                // Get thrust and ISP for flow calculations
                float thrust = 0f;
                float isp = 0f;

                if (node.HasValue(thrustRating) && float.TryParse(node.GetValue(thrustRating), out float maxThrust))
                    thrust = TechLevels.ThrustTL(node.GetValue(thrustRating), node) * scale;

                if (node.HasNode("atmosphereCurve"))
                {
                    var atmCurve = new FloatCurve();
                    atmCurve.Load(node.GetNode("atmosphereCurve"));
                    isp = atmCurve.Evaluate(0f); // Vacuum ISP
                }

                // Calculate total mass flow: F = mdot * Isp * g0
                // Thrust is in kN (kilonewtons), convert to N (newtons) for the equation
                const float g0 = 9.80665f;
                float thrustN = thrust * 1000f;
                float totalMassFlow = (thrustN > 0f && isp > 0f) ? thrustN / (isp * g0) : 0f;

                // Get propellant ratios
                var propNodes = node.GetNodes("PROPELLANT");
                float totalRatio = 0f;
                foreach (var propNode in propNodes)
                {
                    string ratioStr = null;
                    if (propNode.TryGetValue("ratio", ref ratioStr) && float.TryParse(ratioStr, out float ratio))
                        totalRatio += ratio;
                }

                var propellantLines = new List<string>();
                foreach (var propNode in propNodes)
                {
                    string name = propNode.GetValue("name");
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    string line = $"  • {name}";

                    string ratioStr2 = null;
                    if (propNode.TryGetValue("ratio", ref ratioStr2) && float.TryParse(ratioStr2, out float ratio) && totalMassFlow > 0f && totalRatio > 0f)
                    {
                        float propMassFlow = totalMassFlow * (ratio / totalRatio);

                        // Get density from resource library
                        var resource = PartResourceLibrary.Instance?.GetDefinition(name);

                        // Format mass flow: use grams if < 1 kg/s for better precision
                        string massFlowStr = propMassFlow >= 1f
                            ? $"{propMassFlow:F2} kg/s"
                            : $"{propMassFlow * 1000f:F1} g/s";

                        if (resource != null)
                        {
                            float volumeFlow = propMassFlow / (float)resource.density;
                            line += $": {volumeFlow:F2} L/s ({massFlowStr})";
                        }
                        else
                        {
                            line += $": {massFlowStr}";
                        }
                    }

                    propellantLines.Add(line);
                }

                if (propellantLines.Count > 0)
                    tooltipParts.Add($"<b>Propellant Consumption:</b>\n{string.Join("\n", propellantLines)}");
            }

            return tooltipParts.Count > 0 ? string.Join("\n\n", tooltipParts) : string.Empty;
        }

        /// <summary>
        /// Ensures textures are initialized. Handles Unity texture destruction on scene changes.
        /// </summary>
        private void EnsureTexturesAndStyles()
        {
            Textures.EnsureInitialized();
            EngineConfigStyles.Initialize();
        }

        virtual protected void DrawConfigSelectors(IEnumerable<ConfigNode> availableConfigNodes)
        {
            DrawConfigTable(BuildConfigRows());
        }

        private void EngineManagerGUI(int WindowID)
        {
            // Use BeginVertical with GUILayout.ExpandHeight(false) to prevent extra vertical space
            GUILayout.BeginVertical(GUILayout.ExpandHeight(false));

            GUILayout.Space(4); // Minimal top padding

            GUILayout.BeginHorizontal();
            GUILayout.Label(EditorDescription);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(compactView ? "Full View" : "Compact View", GUILayout.Width(100)))
            {
                compactView = !compactView;
            }
            if (GUILayout.Button("Settings", GUILayout.Width(70)))
            {
                showColumnMenu = !showColumnMenu;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4); // Minimal space before table
            DrawConfigSelectors(FilteredDisplayConfigs(false));

            // Draw failure probability chart for current config
            if (config != null && config.HasValue("cycleReliabilityStart"))
            {
                GUILayout.Space(6);

                // Update chart settings from instance fields
                Chart.UseLogScaleX = useLogScaleX;
                Chart.UseLogScaleY = useLogScaleY;
                Chart.UseSimulatedData = useSimulatedData;
                Chart.SimulatedDataValue = simulatedDataValue;
                Chart.ClusterSize = clusterSize;
                Chart.ClusterSizeInput = clusterSizeInput;
                Chart.DataValueInput = dataValueInput;

                // Draw the chart
                Chart.Draw(config, guiWindowRect.width - 10, 360);

                // Update instance fields from chart (for UI controls)
                useLogScaleX = Chart.UseLogScaleX;
                useLogScaleY = Chart.UseLogScaleY;
                useSimulatedData = Chart.UseSimulatedData;
                simulatedDataValue = Chart.SimulatedDataValue;
                clusterSize = Chart.ClusterSize;
                clusterSizeInput = Chart.ClusterSizeInput;
                dataValueInput = Chart.DataValueInput;

                GUILayout.Space(6); // Consistent small space after chart
            }

            TechLevels.DrawTechLevelSelector();

            GUILayout.Space(4); // Minimal bottom padding

            GUILayout.EndVertical();

            if (!myToolTip.Equals(string.Empty) && GUI.tooltip.Equals(string.Empty))
            {
                if (counterTT > 4)
                {
                    myToolTip = GUI.tooltip;
                    counterTT = 0;
                }
                else
                {
                    counterTT++;
                }
            }
            else
            {
                myToolTip = GUI.tooltip;
                counterTT = 0;
            }

            GUI.DragWindow();
        }

        #endregion

        #region Helpers
        public int DoForEachSymmetryCounterpart(Action<ModuleEngineConfigsBase> action)
        {
            int i = 0;
            int mIdx = moduleIndex;
            if (engineID == string.Empty && mIdx < 0)
                mIdx = 0;

            foreach (Part p in part.symmetryCounterparts)
            {
                if (GetSpecifiedModule(p, engineID, mIdx, GetType().Name, false) is ModuleEngineConfigsBase engine)
                {
                    action(engine);
                    ++i;
                }
            }
            return i;
        }

        virtual public int UpdateSymmetryCounterparts()
        {
            return DoForEachSymmetryCounterpart((engine) =>
            {
                engine.techLevel = techLevel;
                engine.SetConfiguration(configuration);
            });
        }

        virtual protected void UpdateOtherModules(ConfigNode node)
        {
            if (node.HasNode("OtherModules"))
            {
                node = node.GetNode("OtherModules");
                for (int i = 0; i < node.values.Count; ++i)
                {
                    if (GetSpecifiedModule(part, node.values[i].name, -1, GetType().Name, false) is ModuleEngineConfigsBase otherM)
                    {
                        otherM.techLevel = techLevel;
                        otherM.SetConfiguration(node.values[i].value);
                    }
                }
            }
        }
        virtual public void CheckConfigs()
        {
            if (configs == null || configs.Count == 0)
                ConfigSaveLoad();
        }
        // run this to save/load non-serialized data
        protected void ConfigSaveLoad()
        {
            string partName = Utilities.GetPartName(part) + moduleIndex + engineID;
            if (configs.Count > 0)
            {
                if (!RFSettings.Instance.engineConfigs.ContainsKey(partName))
                {
                    if (configs.Count > 0)
                        RFSettings.Instance.engineConfigs[partName] = new List<ConfigNode>(configs);
                }
            }
            else if (RFSettings.Instance.engineConfigs.ContainsKey(partName))
                configs = new List<ConfigNode>(RFSettings.Instance.engineConfigs[partName]);
            else
                Debug.LogError($"*RFMEC* ERROR: could not find configs definition for {partName}");
        }

        protected static PartModule GetSpecifiedModule(Part p, string eID, int mIdx, string eType, bool weakType) => GetSpecifiedModules(p, eID, mIdx, eType, weakType).FirstOrDefault();

        private static readonly List<PartModule> _searchList = new List<PartModule>();
        internal static List<PartModule> GetSpecifiedModules(Part p, string eID, int mIdx, string eType, bool weakType)
        {
            int mCount = p.Modules.Count;
            int tmpIdx = 0;
            _searchList.Clear();

            for (int m = 0; m < mCount; ++m)
            {
                PartModule pM = p.Modules[m];
                bool test = false;
                if (weakType)
                {
                    if (eType.Contains("ModuleEngines"))
                        test = pM is ModuleEngines;
                    else if (eType.Contains("ModuleRCS"))
                        test = pM is ModuleRCS;
                }
                else
                    test = pM.GetType().Name.Equals(eType);

                if (test)
                {
                    if (mIdx >= 0)
                    {
                        if (tmpIdx == mIdx)
                        {
                            _searchList.Add(pM);
                        }
                        tmpIdx++;
                        continue; // skip the next test
                    }
                    else if (eID != string.Empty)
                    {
                        string testID = string.Empty;
                        if (pM is ModuleEngines)
                            testID = (pM as ModuleEngines).engineID;
                        else if (pM is ModuleEngineConfigsBase)
                            testID = (pM as ModuleEngineConfigsBase).engineID;

                        if (testID.Equals(eID))
                            _searchList.Add(pM);
                    }
                    else
                        _searchList.Add(pM);
                }
            }
            return _searchList;
        }

        internal void MarkWindowDirty()
        {
            if (UIPartActionController.Instance?.GetItem(part) is UIPartActionWindow action_window)
                action_window.displayDirty = true;
        }
        #endregion

        /// <summary>
        /// Called from RP0KCT when adding vessels to queue to validate whether all the currently selected configs are available and unlocked.
        /// </summary>
        /// <param name="validationError"></param>
        /// <param name="canBeResolved"></param>
        /// <param name="costToResolve"></param>
        /// <returns></returns>
        public virtual bool Validate(out string validationError, out bool canBeResolved, out float costToResolve, out string techToResolve)
        {
            validationError = null;
            canBeResolved = false;
            costToResolve = 0;
            techToResolve = null;

            ConfigNode node = GetConfigByName(configuration);

            if (EngineConfigTechLevels.UnlockedConfig(node, part)) return true;

            techToResolve = config.GetValue("techRequired");
            if (!EngineConfigTechLevels.CanConfig(node))
            {
                validationError = $"{Localizer.GetStringByTag("#RF_Engine_unlocktech")} {ResearchAndDevelopment.GetTechnologyTitle(techToResolve)}"; // unlock tech
                canBeResolved = false;
            }
            else
            {
                validationError = Localizer.GetStringByTag("#RF_Engine_PayEntryCost"); // $"pay entry cost"
                canBeResolved = true;
            }

            string nName = node.GetValue("name");
            double upgradeCost = EntryCostManager.Instance.ConfigEntryCost(nName);
            costToResolve = (float)upgradeCost;
            return false;
        }

        /// <summary>
        /// Called from RP0KCT to purchase configs that were returned as errors in the Validate() method.
        /// </summary>
        /// <returns></returns>
        public virtual bool ResolveValidationError()
        {
            ConfigNode node = GetConfigByName(configuration);
            string nName = node.GetValue("name");
            return EntryCostManager.Instance.PurchaseConfig(nName, node.GetValue("techRequired"));
        }
    }
}
