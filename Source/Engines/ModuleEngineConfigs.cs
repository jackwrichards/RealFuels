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
        private static FieldInfo MRCSConsumedResources = typeof(ModuleRCS).GetField("consumedResources", BindingFlags.NonPublic | BindingFlags.Instance);

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

        #region TestFlight

        public void UpdateTFInterops()
        {
            TestFlightWrapper.AddInteropValue(part, isMaster ? "engineConfig" : "vernierConfig", configuration, "RealFuels");
        }
        #endregion

        #region B9PartSwitch
        protected static bool _b9psReflectionInitialized = false;
        protected static FieldInfo B9PS_moduleID;
        protected static MethodInfo B9PS_SwitchSubtype;
        protected static FieldInfo B9PS_switchInFlight;
        public Dictionary<string, PartModule> B9PSModules;
        protected Dictionary<string, string> RequestedB9PSVariants = new Dictionary<string, string>();

        private void InitializeB9PSReflection()
        {
            if (_b9psReflectionInitialized || !Utilities.B9PSFound) return;
            B9PS_moduleID = Type.GetType("B9PartSwitch.CustomPartModule, B9PartSwitch")?.GetField("moduleID");
            B9PS_SwitchSubtype = Type.GetType("B9PartSwitch.ModuleB9PartSwitch, B9PartSwitch")?.GetMethod("SwitchSubtype");
            B9PS_switchInFlight = Type.GetType("B9PartSwitch.ModuleB9PartSwitch, B9PartSwitch")?.GetField("switchInFlight");
            _b9psReflectionInitialized = true;
        }

        private void LoadB9PSModules()
        {
            IEnumerable<string> b9psModuleIDs = configs
                .Where(cfg => cfg.HasNode("LinkB9PSModule"))
                .SelectMany(cfg => cfg.GetNodes("LinkB9PSModule"))
                .Select(link => link?.GetValue("name"))
                .Where(moduleID => moduleID != null)
                .Distinct();

            B9PSModules = new Dictionary<string, PartModule>(b9psModuleIDs.Count());

            foreach (string moduleID in b9psModuleIDs)
            {
                var module = GetSpecifiedModules(part, string.Empty, -1, "ModuleB9PartSwitch", false)
                    .FirstOrDefault(m => (string)B9PS_moduleID?.GetValue(m) == moduleID);
                if (module == null)
                    Debug.LogError($"*RFMEC* B9PartSwitch module with ID {moduleID} was not found for {part}!");
                else
                    B9PSModules[moduleID] = module;
            }
        }

        /// <summary>
        /// Hide the GUI for all `ModuleB9PartSwitch`s managed by RF.
        /// This is somewhat of a hack-ish approach...
        /// </summary>
        private void HideB9PSVariantSelectors()
        {
            if (B9PSModules == null) return;
            foreach (var module in B9PSModules.Values)
            {
                module.Fields["currentSubtypeTitle"].guiActive = false;
                module.Fields["currentSubtypeTitle"].guiActiveEditor = false;
                module.Fields["currentSubtypeIndex"].guiActive = false;
                module.Fields["currentSubtypeIndex"].guiActiveEditor = false;
                module.Events["ShowSubtypesWindow"].guiActive = false;
                module.Events["ShowSubtypesWindow"].guiActiveEditor = false;
            }
        }

        private IEnumerator HideB9PSInFlightSelector_Coroutine(PartModule module)
        {
            yield return null;
            module.Events["ShowSubtypesWindow"].guiActive = false;
        }

        protected void RequestB9PSVariantsForConfig(ConfigNode node)
        {
            if (B9PSModules == null || B9PSModules.Count == 0) return;
            RequestedB9PSVariants.Clear();
            if (node.GetNodes("LinkB9PSModule") is ConfigNode[] links)
            {
                foreach (ConfigNode link in links)
                {
                    string moduleID = null, subtype = null;
                    if (!link.TryGetValue("name", ref moduleID))
                        Debug.LogError($"*RFMEC* Config `{configurationDisplay}` of {part} has a LinkB9PSModule specification without a name key!");
                    if (!link.TryGetValue("subtype", ref subtype))
                        Debug.LogError($"*RFMEC* Config `{configurationDisplay}` of {part} has a LinkB9PSModule specification without a subtype key!");
                    if (moduleID != null && subtype != null)
                        RequestedB9PSVariants[moduleID] = subtype;
                }
            }
            StartCoroutine(ApplyRequestedB9PSVariants_Coroutine());
        }

        protected IEnumerator ApplyRequestedB9PSVariants_Coroutine()
        {
            yield return new WaitForEndOfFrame();

            if (RequestedB9PSVariants.Count == 0) yield break;

            foreach (var entry in B9PSModules)
            {
                string moduleID = entry.Key;
                PartModule module = entry.Value;

                if (HighLogic.LoadedSceneIsFlight
                    && B9PS_switchInFlight != null
                    && !(bool)B9PS_switchInFlight.GetValue(module)) continue;

                if (!RequestedB9PSVariants.TryGetValue(moduleID, out string subtypeName))
                {
                    Debug.LogError($"*RFMEC* Config {configurationDisplay} of {part} does not specify a subtype for linked B9PS module with ID {moduleID}; defaulting to `{configuration}`.");
                    subtypeName = configuration;
                }

                B9PS_SwitchSubtype?.Invoke(module, new object[] { subtypeName });
                if (HighLogic.LoadedSceneIsFlight) StartCoroutine(HideB9PSInFlightSelector_Coroutine(module));
            }

            RequestedB9PSVariants.Clear();
            // Clear symmetry counterparts' queues since B9PS already handles symmetry.
            DoForEachSymmetryCounterpart(mec => mec.RequestedB9PSVariants.Clear());
        }

        public void UpdateB9PSVariants() => RequestB9PSVariantsForConfig(config);
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
            InitializeB9PSReflection();
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

            LoadB9PSModules();

            LoadDefaultGimbals();

            SetConfiguration();

            Fields[nameof(showRFGUI)].guiName = GUIButtonName;

            // Why is this here, if KSP will call this normally?
            part.Modules.GetModule("ModuleEngineIgnitor")?.OnStart(state);
        }

        public override void OnStartFinished(StartState state)
        {
            HideB9PSVariantSelectors();
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
                info.Append($"  {Utilities.FormatThrust(scale * ThrustTL(config.GetValue(thrustRating), config))}");
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

            ClearFloatCurves(mType, pModule, config, techLevel);
            ClearPropellantGauges(mType, pModule);

            if (type.Equals("ModuleRCS") || type.Equals("ModuleRCSFX"))
                ClearRCSPropellants(config);
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
                        Ignitions = ConfigIgnitions(tmpIgnitions);
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

            UpdateB9PSVariants();

            UpdateTFInterops(); // update TestFlight if it's installed

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

        virtual protected int ConfigIgnitions(int ignitions)
        {
            if (ignitions < 0)
            {
                ignitions = techLevel + ignitions;
                if (ignitions < 1)
                    ignitions = 1;
            }
            else if (ignitions == 0 && !literalZeroIgnitions)
                ignitions = -1;
            return ignitions;
        }

        #region SetConfiguration Tools
        private void ClearFloatCurves(Type mType, PartModule pm, ConfigNode cfg, int techLevel)
        {
            // clear all FloatCurves we need to clear (i.e. if our config has one, or techlevels are enabled)
            bool delAtmo = cfg.HasNode("atmosphereCurve") || techLevel >= 0;
            bool delDens = cfg.HasNode("atmCurve") || techLevel >= 0;
            bool delVel = cfg.HasNode("velCurve") || techLevel >= 0;
            foreach (FieldInfo field in mType.GetFields())
            {
                if (field.FieldType == typeof(FloatCurve) &&
                    ((field.Name.Equals("atmosphereCurve") && delAtmo)
                    || (field.Name.Equals("atmCurve") && delDens)
                    || (field.Name.Equals("velCurve") && delVel)))
                {
                    field.SetValue(pm, new FloatCurve());
                }
            }
        }

        private void ClearPropellantGauges(Type mType, PartModule pm)
        {
            foreach (FieldInfo field in mType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (field.FieldType == typeof(Dictionary<Propellant, ProtoStageIconInfo>) &&
                    field.GetValue(pm) is Dictionary<Propellant, ProtoStageIconInfo> boxes)
                {
                    foreach (ProtoStageIconInfo v in boxes.Values)
                    {
                        try
                        {
                            if (v is ProtoStageIconInfo)
                                pm.part.stackIcon.RemoveInfo(v);
                        }
                        catch (Exception e)
                        {
                            Debug.LogError("*RFMEC* Trying to remove info box: " + e.Message);
                        }
                    }
                    boxes.Clear();
                }
            }
        }

        private void ClearRCSPropellants(ConfigNode cfg)
        {
            List<ModuleRCS> RCSModules = part.Modules.OfType<ModuleRCS>().ToList();
            if (RCSModules.Count > 0)
            {
                DoConfig(cfg);
                foreach (var rcsModule in RCSModules)
                {
                    if (cfg.HasNode("PROPELLANT"))
                        rcsModule.propellants.Clear();
                    rcsModule.Load(cfg);
                    List<PartResourceDefinition> res = MRCSConsumedResources.GetValue(rcsModule) as List<PartResourceDefinition>;
                    res.Clear();
                    foreach (Propellant p in rcsModule.propellants)
                        res.Add(p.resourceDef);
                }
            }
        }

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
                        ignitions = ConfigIgnitions(ignitions);
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
                    configHeat = MassTL(configHeat);

                // set thrust and throttle
                if (configMaxThrust >= 0)
                {
                    configMaxThrust = ThrustTL(configMaxThrust);
                    if (configMinThrust >= 0)
                        configMinThrust = ThrustTL(configMinThrust);
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
                        TLMassMult = MassTL(1.0f);
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
                cost = scale * CostTL(cost, cfg);
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

        #region TechLevel and Required
        /// <summary>
        /// Is this config unlocked? Note: Is the same as CanConfig when not CAREER and no upgrade manager instance.
        /// </summary>
        /// <param name="config"></param>
        /// <returns></returns>
        public static bool UnlockedConfig(ConfigNode config, Part p)
        {
            if (config == null)
                return false;
            if (!config.HasValue("name"))
                return false;
            if (EntryCostManager.Instance != null && HighLogic.CurrentGame != null && HighLogic.CurrentGame.Mode != Game.Modes.SANDBOX)
                return EntryCostManager.Instance.ConfigUnlocked((RFSettings.Instance.usePartNameInConfigUnlock ? Utilities.GetPartName(p) : string.Empty) + config.GetValue("name"));
            return true;
        }
        public static bool CanConfig(ConfigNode config)
        {
            if (config == null)
                return false;
            if (!config.HasValue("techRequired") || HighLogic.CurrentGame == null)
                return true;
            if (HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX || ResearchAndDevelopment.GetTechnologyState(config.GetValue("techRequired")) == RDTech.State.Available)
                return true;
            return false;
        }
        public static bool UnlockedTL(string tlName, int newTL)
        {
            if (EntryCostManager.Instance != null && HighLogic.CurrentGame != null && HighLogic.CurrentGame.Mode != Game.Modes.SANDBOX)
                return EntryCostManager.Instance.TLUnlocked(tlName) >= newTL;
            return true;
        }

        private double ThrustTL(ConfigNode cfg = null)
        {
            if (techLevel != -1 && !engineType.Contains("S"))
            {
                TechLevel oldTL = new TechLevel(), newTL = new TechLevel();
                if (oldTL.Load(cfg ?? config, techNodes, engineType, origTechLevel) &&
                    newTL.Load(cfg ?? config, techNodes, engineType, techLevel))
                    return newTL.Thrust(oldTL);
            }
            return 1;
        }

        private float ThrustTL(float thrust, ConfigNode cfg = null)
        {
            return (float)Math.Round(thrust * ThrustTL(cfg), 6);
        }

        private float ThrustTL(string thrust, ConfigNode cfg = null)
        {
            float.TryParse(thrust, out float tmp);
            return ThrustTL(tmp, cfg);
        }

        private double MassTL(ConfigNode cfg = null)
        {
            if (techLevel != -1)
            {
                TechLevel oldTL = new TechLevel(), newTL = new TechLevel();
                if (oldTL.Load(cfg ?? config, techNodes, engineType, origTechLevel) &&
                    newTL.Load(cfg ?? config, techNodes, engineType, techLevel))
                    return newTL.Mass(oldTL, engineType.Contains("S"));
            }
            return 1;
        }

        private float MassTL(float mass)
        {
            return (float)Math.Round(mass * MassTL(), 6);
        }
        private float CostTL(float cost, ConfigNode cfg = null)
        {
            TechLevel cTL = new TechLevel();
            TechLevel oTL = new TechLevel();
            if (cTL.Load(cfg, techNodes, engineType, techLevel) && oTL.Load(cfg, techNodes, engineType, origTechLevel) && part.partInfo != null)
            {
                // Bit of a dance: we have to figure out the total cost of the part, but doing so
                // also depends on us. So we zero out our contribution first
                // and then restore configCost.
                float oldCC = configCost;
                configCost = 0f;
                float totalCost = part.partInfo.cost + part.GetModuleCosts(part.partInfo.cost);
                configCost = oldCC;
                cost = (totalCost + cost) * (cTL.CostMult / oTL.CostMult) - totalCost;
            }

            return cost;
        }
        #endregion
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
        }

        private static Vector3 mousePos = Vector3.zero;
        private Rect guiWindowRect = new Rect(0, 0, 0, 0);
        private string myToolTip = string.Empty;
        private int counterTT;
        private bool editorLocked = false;

        private Vector2 configScrollPos = Vector2.zero;
        private GUIContent configGuiContent;
        private bool compactView = false;
        private bool useLogScaleX = false; // Toggle for logarithmic x-axis on failure chart
        private bool useLogScaleY = false; // Toggle for logarithmic y-axis on failure chart

        // Simulation controls for data percentage and cluster size
        private bool useSimulatedData = false; // Whether to override real TestFlight data
        private float simulatedDataPercentage = 0f; // Simulated data percentage (0-1)
        private int clusterSize = 1; // Number of engines in cluster (default 1)
        private string clusterSizeInput = "1"; // Text input for cluster size
        private string dataPercentageInput = "0"; // Text input for data percentage

        private const int ConfigRowHeight = 22;
        private const int ConfigMaxVisibleRows = 16; // Max rows before scrolling (60% taller)
        // Dynamic column widths - calculated based on content
        private float[] ConfigColumnWidths = new float[18];

        private static Texture2D rowHoverTex;
        private static Texture2D rowCurrentTex;
        private static Texture2D rowLockedTex;
        private static Texture2D zebraStripeTex;
        private static Texture2D columnSeparatorTex;

        // Chart textures - cached to prevent loss on focus change
        private static Texture2D chartBgTex;
        private static Texture2D chartGridMajorTex;
        private static Texture2D chartGridMinorTex;
        private static Texture2D chartGreenZoneTex;
        private static Texture2D chartYellowZoneTex;
        private static Texture2D chartRedZoneTex;
        private static Texture2D chartDarkRedZoneTex;
        private static Texture2D chartStartupZoneTex;
        private static Texture2D chartLineTex;
        private static Texture2D chartMarkerBlueTex;
        private static Texture2D chartMarkerGreenTex;
        private static Texture2D chartMarkerYellowTex;
        private static Texture2D chartMarkerOrangeTex;
        private static Texture2D chartMarkerDarkRedTex;
        private static Texture2D chartSeparatorTex;
        private static Texture2D chartHoverLineTex;
        private static Texture2D chartOrangeLineTex;
        private static Texture2D chartGreenLineTex;
        private static Texture2D chartBlueLineTex;
        private static Texture2D chartTooltipBgTex;
        private static Texture2D infoPanelBgTex;

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
                // Set position, width and height will auto-size based on content
                guiWindowRect = new Rect(posAdd + 430 * posMult, 365, 100, 0); // Start small, will grow
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
                    curCost = CostTL(curCost, node) - CostTL(0f, node); // get purely the config cost difference
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

                    if (!UnlockedConfig(node, part))
                    {
                        double upgradeCost = EntryCostManager.Instance.ConfigEntryCost(nName);
                        string techRequired = node.GetValue("techRequired");
                        if (upgradeCost <= 0)
                        {
                            // Auto-buy.
                            EntryCostManager.Instance.PurchaseConfig(nName, techRequired);
                        }

                        bool isConfigAvailable = CanConfig(node);
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
                    if (!CanConfig(node))
                    {
                        if (techNameToTitle.TryGetValue(node.GetValue("techRequired"), out string techStr))
                            techStr = $"\n{Localizer.GetStringByTag("#RF_Engine_Requires")}: " + techStr; // Requires
                        GUILayout.Label(new GUIContent(Localizer.Format("#RF_Engine_LacksTech", dispName), configInfo + techStr)); // $"Lacks tech for {dispName}"
                        return;
                    }

                    // Available.
                    if (UnlockedConfig(node, part))
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
            EnsureTableTextures();

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
            guiWindowRect.width = requiredWindowWidth;

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

                bool isLocked = !CanConfig(row.Node);
                if (Event.current.type == EventType.Repaint)
                {
                    // Draw alternating row background first
                    if (!row.IsSelected && !isLocked && !isHovered && rowIndex % 2 == 1)
                    {
                        GUI.DrawTexture(tableRowRect, zebraStripeTex);
                    }

                    if (row.IsSelected)
                        GUI.DrawTexture(tableRowRect, rowCurrentTex);
                    else if (isLocked)
                        GUI.DrawTexture(tableRowRect, rowLockedTex);
                    else if (isHovered)
                        GUI.DrawTexture(tableRowRect, rowHoverTex);
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
                    GUI.DrawTexture(separatorRect, columnSeparatorTex);
                }
            }
        }

        private void DrawHeaderCell(Rect rect, string text, string tooltip)
        {
            bool hover = rect.Contains(Event.current.mousePosition);
            var headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = hover ? 15 : 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f) },
                alignment = TextAnchor.LowerLeft,
                richText = true
            };
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
            var primaryStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = isHovered ? 15 : 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = isLocked ? new Color(1f, 0.65f, 0.3f) : new Color(0.85f, 0.85f, 0.85f) },
                alignment = TextAnchor.MiddleLeft,
                richText = true,
                padding = new RectOffset(5, 0, 0, 0) // Add left padding
            };
            var secondaryStyle = new GUIStyle(primaryStyle)
            {
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }
            };

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
            var buttonStyle = HighLogic.Skin.button;
            var smallButtonStyle = new GUIStyle(buttonStyle)
            {
                fontSize = 11,
                padding = new RectOffset(2, 2, 2, 2)
            };

            string configName = node.GetValue("name");
            bool canUse = CanConfig(node);
            bool unlocked = UnlockedConfig(node, part);
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

        private string GetThrustString(ConfigNode node)
        {
            if (!node.HasValue(thrustRating))
                return "-";

            float thrust = scale * ThrustTL(node.GetValue(thrustRating), node);
            // Remove decimals for large thrust values
            if (thrust >= 100f)
                return $"{thrust:N0} kN";
            return $"{thrust:N2} kN";
        }

        private string GetMinThrottleString(ConfigNode node)
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

        private string GetIspString(ConfigNode node)
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

        private string GetMassString(ConfigNode node)
        {
            if (origMass <= 0f)
                return "-";

            float cMass = scale * origMass * RFSettings.Instance.EngineMassMultiplier;
            if (node.HasValue("massMult") && float.TryParse(node.GetValue("massMult"), out float ftmp))
                cMass *= ftmp;

            return $"{cMass:N3}t";
        }

        private string GetGimbalString(ConfigNode node)
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

        private string GetIgnitionsString(ConfigNode node)
        {
            if (!node.HasValue("ignitions"))
                return "-";

            if (!int.TryParse(node.GetValue("ignitions"), out int ignitions))
                return "∞";

            int resolved = ConfigIgnitions(ignitions);
            if (resolved == -1)
                return "∞";
            if (resolved == 0 && literalZeroIgnitions)
                return "<color=#FFEB3B>Gnd</color>"; // Yellow G for ground-only ignitions
            return resolved.ToString();
        }

        private string GetBoolSymbol(ConfigNode node, string key)
        {
            if (!node.HasValue(key))
                return "<color=#9E9E9E>✗</color>"; // Treat missing as false - gray (no restriction)
            bool isTrue = node.GetValue(key).ToLower() == "true";
            return isTrue ? "<color=#FFA726>✓</color>" : "<color=#9E9E9E>✗</color>"; // Orange for restriction, gray for no restriction
        }

        private bool IsColumnVisible(int columnIndex)
        {
            if (!compactView)
                return true; // All columns visible in full view

            // Compact view: show only essential columns
            // 0: Name, 1: Thrust, 3: ISP, 4: Mass, 6: Ignitions, 9: Rated Burn, 10: Tested Burn, 15: Tech, 16: Cost, 17: Actions
            return columnIndex == 0 || columnIndex == 1 || columnIndex == 3 || columnIndex == 4 ||
                   columnIndex == 6 || columnIndex == 9 || columnIndex == 10 || columnIndex == 15 || columnIndex == 16 || columnIndex == 17;
        }

        private string GetRatedBurnTimeString(ConfigNode node)
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

        private string GetTestedBurnTimeString(ConfigNode node)
        {
            // Values are copied to CONFIG level by ModuleManager patch
            if (!node.HasValue("testedBurnTime"))
                return "-";

            float testedBurnTime = 0f;
            if (node.TryGetValue("testedBurnTime", ref testedBurnTime))
                return testedBurnTime.ToString("F0");

            return "-";
        }

        private string GetIgnitionReliabilityStartString(ConfigNode node)
        {
            // Values are copied to CONFIG level by ModuleManager patch
            if (!node.HasValue("ignitionReliabilityStart"))
                return "-";
            if (float.TryParse(node.GetValue("ignitionReliabilityStart"), out float val))
                return $"{val:P1}";
            return "-";
        }

        private string GetIgnitionReliabilityEndString(ConfigNode node)
        {
            // Values are copied to CONFIG level by ModuleManager patch
            if (!node.HasValue("ignitionReliabilityEnd"))
                return "-";
            if (float.TryParse(node.GetValue("ignitionReliabilityEnd"), out float val))
                return $"{val:P1}";
            return "-";
        }

        private string GetCycleReliabilityStartString(ConfigNode node)
        {
            // Values are copied to CONFIG level by ModuleManager patch
            if (!node.HasValue("cycleReliabilityStart"))
                return "-";
            if (float.TryParse(node.GetValue("cycleReliabilityStart"), out float val))
                return $"{val:P1}";
            return "-";
        }

        private string GetCycleReliabilityEndString(ConfigNode node)
        {
            // Values are copied to CONFIG level by ModuleManager patch
            if (!node.HasValue("cycleReliabilityEnd"))
                return "-";
            if (float.TryParse(node.GetValue("cycleReliabilityEnd"), out float val))
                return $"{val:P1}";
            return "-";
        }

        private string GetTechString(ConfigNode node)
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

        private string GetCostDeltaString(ConfigNode node)
        {
            if (!node.HasValue("cost"))
                return "-";

            float curCost = scale * float.Parse(node.GetValue("cost"), CultureInfo.InvariantCulture);
            if (techLevel != -1)
                curCost = CostTL(curCost, node) - CostTL(0f, node);

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
                    thrust = ThrustTL(node.GetValue(thrustRating), node) * scale;

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

        private void EnsureTableTextures()
        {
            // Use Unity's implicit bool conversion to properly detect destroyed textures
            if (!rowHoverTex)
                rowHoverTex = Styles.CreateColorPixel(new Color(1f, 1f, 1f, 0.05f));
            if (!rowCurrentTex)
                rowCurrentTex = Styles.CreateColorPixel(new Color(0.3f, 0.6f, 1.0f, 0.20f)); // Subtle blue tint
            if (!rowLockedTex)
                rowLockedTex = Styles.CreateColorPixel(new Color(1f, 0.5f, 0.3f, 0.15f)); // Subtle orange tint
            if (!zebraStripeTex)
                zebraStripeTex = Styles.CreateColorPixel(new Color(0.05f, 0.05f, 0.05f, 0.3f));
            if (!columnSeparatorTex)
                columnSeparatorTex = Styles.CreateColorPixel(new Color(0.25f, 0.25f, 0.25f, 0.9f));
        }

        private void EnsureChartTextures()
        {
            // Use Unity's implicit bool conversion to properly detect destroyed textures
            if (!chartBgTex)
                chartBgTex = Styles.CreateColorPixel(new Color(0.1f, 0.1f, 0.1f, 0.8f));
            if (!chartGridMajorTex)
                chartGridMajorTex = Styles.CreateColorPixel(new Color(0.3f, 0.3f, 0.3f, 0.4f)); // Major gridlines at 20%
            if (!chartGridMinorTex)
                chartGridMinorTex = Styles.CreateColorPixel(new Color(0.25f, 0.25f, 0.25f, 0.2f)); // Minor gridlines at 10%, barely visible
            if (!chartGreenZoneTex)
                chartGreenZoneTex = Styles.CreateColorPixel(new Color(0.2f, 0.5f, 0.2f, 0.15f));
            if (!chartYellowZoneTex)
                chartYellowZoneTex = Styles.CreateColorPixel(new Color(0.5f, 0.5f, 0.2f, 0.15f));
            if (!chartRedZoneTex)
                chartRedZoneTex = Styles.CreateColorPixel(new Color(0.5f, 0.2f, 0.2f, 0.15f));
            if (!chartDarkRedZoneTex)
                chartDarkRedZoneTex = Styles.CreateColorPixel(new Color(0.4f, 0.1f, 0.1f, 0.25f)); // Darker red for 100× zone
            if (!chartStartupZoneTex)
                chartStartupZoneTex = Styles.CreateColorPixel(new Color(0.15f, 0.3f, 0.5f, 0.3f));
            if (!chartLineTex)
                chartLineTex = Styles.CreateColorPixel(new Color(0.8f, 0.4f, 0.4f, 1f));
            if (!chartMarkerBlueTex)
                chartMarkerBlueTex = Styles.CreateColorPixel(new Color(0.4f, 0.6f, 0.9f, 0.5f)); // Blue for startup zone end
            if (!chartMarkerGreenTex)
                chartMarkerGreenTex = Styles.CreateColorPixel(new Color(0.3f, 0.8f, 0.3f, 0.5f)); // Less prominent
            if (!chartMarkerYellowTex)
                chartMarkerYellowTex = Styles.CreateColorPixel(new Color(0.9f, 0.9f, 0.3f, 0.5f)); // Less prominent
            if (!chartMarkerOrangeTex)
                chartMarkerOrangeTex = Styles.CreateColorPixel(new Color(1f, 0.65f, 0f, 0.5f)); // Less prominent
            if (!chartMarkerDarkRedTex)
                chartMarkerDarkRedTex = Styles.CreateColorPixel(new Color(0.8f, 0.1f, 0.1f, 0.5f)); // Less prominent
            if (!chartSeparatorTex)
                chartSeparatorTex = Styles.CreateColorPixel(new Color(0.6f, 0.6f, 0.6f, 0.5f)); // Less prominent
            if (!chartHoverLineTex)
                chartHoverLineTex = Styles.CreateColorPixel(new Color(1f, 1f, 1f, 0.4f));
            if (!chartOrangeLineTex)
                chartOrangeLineTex = Styles.CreateColorPixel(new Color(1f, 0.5f, 0.2f, 1f));
            if (!chartGreenLineTex)
                chartGreenLineTex = Styles.CreateColorPixel(new Color(0.3f, 0.9f, 0.3f, 1f));
            if (!chartBlueLineTex)
                chartBlueLineTex = Styles.CreateColorPixel(new Color(0.5f, 0.85f, 1.0f, 1f)); // Lighter blue for current data
            if (!chartTooltipBgTex)
                chartTooltipBgTex = Styles.CreateColorPixel(new Color(0.1f, 0.1f, 0.1f, 0.95f));
            if (!infoPanelBgTex)
                infoPanelBgTex = Styles.CreateColorPixel(new Color(0.12f, 0.12f, 0.12f, 0.9f));
        }

        /// <summary>
        /// Format MTBF (mean time between failures) in human-readable units.
        /// </summary>
        private string FormatMTBF(float mtbfSeconds)
        {
            if (float.IsInfinity(mtbfSeconds) || float.IsNaN(mtbfSeconds))
                return "∞";

            if (mtbfSeconds < 60f)
                return $"{mtbfSeconds:F1}s";
            if (mtbfSeconds < 3600f)
                return $"{mtbfSeconds / 60f:F1}m";
            if (mtbfSeconds < 86400f)
                return $"{mtbfSeconds / 3600f:F1}h";
            if (mtbfSeconds < 31536000f)
                return $"{mtbfSeconds / 86400f:F1}d";
            return $"{mtbfSeconds / 31536000f:F1}y";
        }

        /// <summary>
        /// Numerically integrate the cycle curve from t1 to t2 using trapezoidal rule.
        /// Returns the integral of the cycle modifier over the time interval.
        /// </summary>
        private float IntegrateCycleCurve(FloatCurve curve, float t1, float t2, int steps)
        {
            if (t2 <= t1) return 0f;

            float dt = (t2 - t1) / steps;
            float sum = 0f;

            // Trapezoidal rule: integrate by averaging adjacent points
            for (int i = 0; i < steps; i++)
            {
                float tStart = t1 + i * dt;
                float tEnd = tStart + dt;
                float valueStart = curve.Evaluate(tStart);
                float valueEnd = curve.Evaluate(tEnd);
                sum += (valueStart + valueEnd) * 0.5f * dt;
            }

            return sum;
        }

        /// <summary>
        /// Build the TestFlight cycle curve exactly as TestFlight_Generic_Engines.cfg does.
        /// This matches the ModuleManager patch logic from RealismOverhaul.
        /// </summary>
        private FloatCurve BuildTestFlightCycleCurve(float ratedBurnTime, float testedBurnTime, float overburnPenalty, bool hasTestedBurnTime)
        {
            FloatCurve curve = new FloatCurve();

            // Key 1: Early burn high penalty
            curve.Add(0.00f, 10.00f);

            // Key 2: Stabilize at 5 seconds
            curve.Add(5.00f, 1.00f, -0.8f, 0f);

            // Key 3: Maintain 1.0 until rated burn time (+ 5 second cushion)
            float rbtCushioned = ratedBurnTime + 5f;
            curve.Add(rbtCushioned, 1f, 0f, 0f);

            if (hasTestedBurnTime)
            {
                // Key 4: Tested burn time with smooth transition
                float ratedToTestedInterval = testedBurnTime - rbtCushioned;
                float tbtTransitionSlope = 3.135f / ratedToTestedInterval;
                float tbtTransitionSlopeMult = overburnPenalty - 1.0f;
                tbtTransitionSlope *= tbtTransitionSlopeMult;
                curve.Add(testedBurnTime, overburnPenalty, tbtTransitionSlope, tbtTransitionSlope);

                // Key 5: Complete failure at 2.5x tested burn time
                float failTime = testedBurnTime * 2.5f;
                float tbtToFailInterval = failTime - testedBurnTime;
                float failInSlope = 1.989f / tbtToFailInterval;
                float failInSlopeMult = 100f - overburnPenalty;
                failInSlope *= failInSlopeMult;
                curve.Add(failTime, 100f, failInSlope, 0f);
            }
            else
            {
                // Key 4: Complete failure at 2.5x rated burn time (standard overburn)
                float failTime = ratedBurnTime * 2.5f;
                float rbtToFailInterval = failTime - rbtCushioned;
                float failInSlope = 292.8f / rbtToFailInterval;
                curve.Add(failTime, 100f, failInSlope, 0f);
            }

            return curve;
        }

        /// <summary>
        /// Convert time to x-position on the chart, using either linear or logarithmic scale.
        /// </summary>
        private float TimeToXPosition(float time, float maxTime, float plotX, float plotWidth, bool useLogScale)
        {
            if (useLogScale)
            {
                // Logarithmic scale: use log10(time + 1) to handle t=0
                // Map from log10(1) to log10(maxTime + 1)
                float logTime = Mathf.Log10(time + 1f);
                float logMax = Mathf.Log10(maxTime + 1f);
                return plotX + (logTime / logMax) * plotWidth;
            }
            else
            {
                // Linear scale
                return plotX + (time / maxTime) * plotWidth;
            }
        }

        /// <summary>
        /// Convert x-position back to time, using either linear or logarithmic scale.
        /// </summary>
        private float XPositionToTime(float xPos, float maxTime, float plotX, float plotWidth, bool useLogScale)
        {
            float normalizedX = (xPos - plotX) / plotWidth;
            normalizedX = Mathf.Clamp01(normalizedX);

            if (useLogScale)
            {
                // Inverse of log scale: 10^(normalizedX * log10(maxTime + 1)) - 1
                float logMax = Mathf.Log10(maxTime + 1f);
                return Mathf.Pow(10f, normalizedX * logMax) - 1f;
            }
            else
            {
                // Linear scale
                return normalizedX * maxTime;
            }
        }

        /// <summary>
        /// Convert failure probability to y-position on the chart, using either linear or logarithmic scale.
        /// </summary>
        private float FailureProbToYPosition(float failureProb, float yAxisMax, float plotY, float plotHeight, bool useLogScale)
        {
            if (useLogScale)
            {
                // Logarithmic scale: use log10(prob + 0.0001) to handle near-zero values
                // The +0.0001 offset prevents log(0) and provides a visible baseline
                float logProb = Mathf.Log10(failureProb + 0.0001f);
                float logMax = Mathf.Log10(yAxisMax + 0.0001f);
                float logMin = Mathf.Log10(0.0001f); // Minimum visible value
                float normalizedLog = (logProb - logMin) / (logMax - logMin);
                return plotY + plotHeight - (normalizedLog * plotHeight);
            }
            else
            {
                // Linear scale
                return plotY + plotHeight - ((failureProb / yAxisMax) * plotHeight);
            }
        }

        private void DrawFailureProbabilityChart(ConfigNode configNode, float width, float height)
        {
            // Ensure textures are cached to prevent loss on window focus change
            EnsureChartTextures();

            // Values are copied to CONFIG level by ModuleManager patch
            // Get TestFlight data for both start and end (we plot both)
            if (!configNode.HasValue("cycleReliabilityStart")) return;
            if (!configNode.HasValue("cycleReliabilityEnd")) return;
            if (!float.TryParse(configNode.GetValue("cycleReliabilityStart"), out float cycleReliabilityStart)) return;
            if (!float.TryParse(configNode.GetValue("cycleReliabilityEnd"), out float cycleReliabilityEnd)) return;

            // Validate reliability is in valid range
            if (cycleReliabilityStart <= 0f || cycleReliabilityStart > 1f) return;
            if (cycleReliabilityEnd <= 0f || cycleReliabilityEnd > 1f) return;

            float ratedBurnTime = 0;
            if (!configNode.TryGetValue("ratedBurnTime", ref ratedBurnTime) || ratedBurnTime <= 0) return;

            float ratedContinuousBurnTime = ratedBurnTime;
            configNode.TryGetValue("ratedContinuousBurnTime", ref ratedContinuousBurnTime);

            // Skip chart if this is a cumulative-limited engine (continuous << total)
            if (ratedContinuousBurnTime < ratedBurnTime * 0.9f) return;

            // Read testedBurnTime to match TestFlight's exact behavior
            float testedBurnTime = 0f;
            bool hasTestedBurnTime = configNode.TryGetValue("testedBurnTime", ref testedBurnTime) && testedBurnTime > ratedBurnTime;

            // Split the area: chart on left (60%), info on right (40%)
            float chartWidth = width * 0.58f;
            float infoWidth = width * 0.42f;

            float overburnPenalty = 2.0f; // Default from TestFlight_Generic_Engines.cfg
            configNode.TryGetValue("overburnPenalty", ref overburnPenalty);

            // Build the actual TestFlight cycle curve
            FloatCurve cycleCurve = BuildTestFlightCycleCurve(ratedBurnTime, testedBurnTime, overburnPenalty, hasTestedBurnTime);

            // Main container
            Rect containerRect = GUILayoutUtility.GetRect(width, height);

            // Chart area (left side)
            const float padding = 38f;
            float plotWidth = chartWidth - padding * 2;
            float plotHeight = height - padding * 2;

            // Extend max time to show the full cycle curve beyond where it reaches 100× modifier
            // The cycle curve reaches maximum at 2.5× (rated or tested), extend to 3.5× to see asymptotic behavior
            float maxTime = hasTestedBurnTime ? testedBurnTime * 3.5f : ratedBurnTime * 3.5f;

            Rect chartRect = new Rect(containerRect.x, containerRect.y, chartWidth, height);
            Rect plotArea = new Rect(chartRect.x + padding, chartRect.y + padding, plotWidth, plotHeight);

            // Info panel area (right side)
            Rect infoRect = new Rect(containerRect.x + chartWidth, containerRect.y, infoWidth, height);

            // Draw background
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(chartRect, chartBgTex);
            }

            // Calculate failure probabilities for both curves to determine Y-axis scale
            const int curvePoints = 100;
            float[] failureProbsStart = new float[curvePoints];
            float[] failureProbsEnd = new float[curvePoints];
            float maxFailureProb = 0f;

            // Base failure rates (from reliability at rated burn time)
            float baseRateStart = -Mathf.Log(cycleReliabilityStart) / ratedBurnTime;
            float baseRateEnd = -Mathf.Log(cycleReliabilityEnd) / ratedBurnTime;

            for (int i = 0; i < curvePoints; i++)
            {
                float t = (i / (float)(curvePoints - 1)) * maxTime;

                // Calculate failure using TestFlight's cycle curve
                // For t <= ratedBurnTime: standard exponential reliability
                // For t > ratedBurnTime: integrate the cycle modifier to account for varying failure rate

                // Calculate for start (0 data)
                float failureProbStart = 0f;
                if (t <= ratedBurnTime)
                {
                    failureProbStart = 1f - Mathf.Pow(cycleReliabilityStart, t / ratedBurnTime);
                }
                else
                {
                    // Base failure up to rated time
                    float survivalToRated = cycleReliabilityStart;

                    // Integrate cycle modifier from ratedBurnTime to t using numerical integration
                    float integratedModifier = IntegrateCycleCurve(cycleCurve, ratedBurnTime, t, 20);

                    // Additional failure rate scaled by integrated modifier
                    float additionalFailRate = baseRateStart * integratedModifier;

                    // Total survival = survive to rated * survive additional time
                    float survivalProb = survivalToRated * Mathf.Exp(-additionalFailRate);
                    failureProbStart = Mathf.Clamp01(1f - survivalProb);
                }
                failureProbsStart[i] = failureProbStart;
                maxFailureProb = Mathf.Max(maxFailureProb, failureProbStart);

                // Calculate for end (max data)
                float failureProbEnd = 0f;
                if (t <= ratedBurnTime)
                {
                    failureProbEnd = 1f - Mathf.Pow(cycleReliabilityEnd, t / ratedBurnTime);
                }
                else
                {
                    // Base failure up to rated time
                    float survivalToRated = cycleReliabilityEnd;

                    // Integrate cycle modifier from ratedBurnTime to t
                    float integratedModifier = IntegrateCycleCurve(cycleCurve, ratedBurnTime, t, 20);

                    // Additional failure rate scaled by integrated modifier
                    float additionalFailRate = baseRateEnd * integratedModifier;

                    // Total survival = survive to rated * survive additional time
                    float survivalProb = survivalToRated * Mathf.Exp(-additionalFailRate);
                    failureProbEnd = Mathf.Clamp01(1f - survivalProb);
                }
                failureProbsEnd[i] = failureProbEnd;
                maxFailureProb = Mathf.Max(maxFailureProb, failureProbEnd);
            }

            // Get current TestFlight data (or use simulated value)
            float realDataPercentage = TestFlightWrapper.GetDataPercentage(part);
            float dataPercentage = useSimulatedData ? simulatedDataPercentage : realDataPercentage;
            bool hasCurrentData = (useSimulatedData && simulatedDataPercentage >= 0f) || (dataPercentage >= 0f && dataPercentage <= 1f);
            float[] failureProbsCurrent = null;
            float cycleReliabilityCurrent = 0f;
            float baseRateCurrent = 0f;

            if (hasCurrentData)
            {
                // Interpolate current reliability between start and end based on data percentage
                cycleReliabilityCurrent = Mathf.Lerp(cycleReliabilityStart, cycleReliabilityEnd, dataPercentage);
                baseRateCurrent = -Mathf.Log(cycleReliabilityCurrent) / ratedBurnTime;
                failureProbsCurrent = new float[curvePoints];

                for (int i = 0; i < curvePoints; i++)
                {
                    float t = (i / (float)(curvePoints - 1)) * maxTime;
                    float failureProbCurrent = 0f;

                    if (t <= ratedBurnTime)
                    {
                        failureProbCurrent = 1f - Mathf.Pow(cycleReliabilityCurrent, t / ratedBurnTime);
                    }
                    else
                    {
                        float survivalToRated = cycleReliabilityCurrent;
                        float integratedModifier = IntegrateCycleCurve(cycleCurve, ratedBurnTime, t, 20);
                        float additionalFailRate = baseRateCurrent * integratedModifier;
                        float survivalProb = survivalToRated * Mathf.Exp(-additionalFailRate);
                        failureProbCurrent = Mathf.Clamp01(1f - survivalProb);
                    }

                    failureProbsCurrent[i] = failureProbCurrent;
                    maxFailureProb = Mathf.Max(maxFailureProb, failureProbCurrent);
                }
            }

            // Apply cluster math: for N engines, probability at least one fails = 1 - (1 - singleFailProb)^N
            if (clusterSize > 1)
            {
                for (int i = 0; i < curvePoints; i++)
                {
                    // Transform each failure probability for cluster
                    float singleSurvival = 1f - failureProbsStart[i];
                    failureProbsStart[i] = 1f - Mathf.Pow(singleSurvival, clusterSize);

                    singleSurvival = 1f - failureProbsEnd[i];
                    failureProbsEnd[i] = 1f - Mathf.Pow(singleSurvival, clusterSize);

                    if (hasCurrentData)
                    {
                        singleSurvival = 1f - failureProbsCurrent[i];
                        failureProbsCurrent[i] = 1f - Mathf.Pow(singleSurvival, clusterSize);
                    }

                    // Update max failure probability after cluster transformation
                    maxFailureProb = Mathf.Max(maxFailureProb, failureProbsStart[i]);
                    maxFailureProb = Mathf.Max(maxFailureProb, failureProbsEnd[i]);
                    if (hasCurrentData)
                        maxFailureProb = Mathf.Max(maxFailureProb, failureProbsCurrent[i]);
                }
            }

            // Set Y-axis max to 2% above the maximum failure probability
            float yAxisMaxRaw = Mathf.Min(1f, maxFailureProb + 0.02f);

            // Round up to a "nice" number for clean axis labels
            float yAxisMax = RoundToNiceNumber(yAxisMaxRaw, true);
            // Ensure minimum range for readability
            if (yAxisMax < 0.05f) yAxisMax = 0.05f;

            // Draw grid lines and labels with dynamic scale
            var labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = Color.grey } };

            if (useLogScaleY)
            {
                // Logarithmic Y-axis labels: 0.01%, 0.1%, 1%, 10%, 100%
                float[] logValues = { 0.0001f, 0.001f, 0.01f, 0.1f, 1f }; // As fractions
                foreach (float failureProb in logValues)
                {
                    if (failureProb > yAxisMax) break; // Don't show labels beyond max

                    float y = FailureProbToYPosition(failureProb, yAxisMax, plotArea.y, plotArea.height, useLogScaleY);
                    Rect lineRect = new Rect(plotArea.x, y, plotArea.width, 1);
                    if (Event.current.type == EventType.Repaint)
                        GUI.DrawTexture(lineRect, chartGridMajorTex);

                    float labelValue = failureProb * 100f;
                    string label = labelValue < 0.1f ? $"{labelValue:F3}%" : (labelValue < 1f ? $"{labelValue:F2}%" : (labelValue < 10f ? $"{labelValue:F1}%" : $"{labelValue:F0}%"));
                    GUI.Label(new Rect(plotArea.x - 35, y - 10, 30, 20), label, labelStyle);
                }
            }
            else
            {
                // Linear Y-axis: Major gridlines at 20% intervals, minor at 10%
                // Draw all gridlines (major + minor)
                for (int i = 0; i <= 10; i++)
                {
                    bool isMajor = (i % 2 == 0); // Major gridlines at 0%, 20%, 40%, 60%, 80%, 100%
                    float y = plotArea.y + plotArea.height - (i * plotArea.height / 10f);
                    Rect lineRect = new Rect(plotArea.x, y, plotArea.width, 1);

                    if (Event.current.type == EventType.Repaint)
                    {
                        // Use major or minor gridline texture
                        GUI.DrawTexture(lineRect, isMajor ? chartGridMajorTex : chartGridMinorTex);
                    }

                    // Only show labels on major gridlines
                    if (isMajor)
                    {
                        float labelValue = (i / 10f) * yAxisMax * 100f;
                        string label = labelValue < 1f ? $"{labelValue:F2}%" : (labelValue < 10f ? $"{labelValue:F1}%" : $"{labelValue:F0}%");
                        GUI.Label(new Rect(plotArea.x - 35, y - 10, 30, 20), label, labelStyle);
                    }
                }
            }

            // Draw zone backgrounds based on TestFlight cycle curve segments
            // Zone boundaries match the cycle curve keys
            float startupEndX = TimeToXPosition(5f, maxTime, plotArea.x, plotArea.width, useLogScaleX); // End of startup zone (0-5s)
            float ratedCushionedX = TimeToXPosition(ratedBurnTime + 5f, maxTime, plotArea.x, plotArea.width, useLogScaleX); // Rated + 5s cushion
            float testedX = hasTestedBurnTime ? TimeToXPosition(testedBurnTime, maxTime, plotArea.x, plotArea.width, useLogScaleX) : 0f;

            // Calculate 100× modifier point (at 2.5× the reference burn time)
            float referenceBurnTime = hasTestedBurnTime ? testedBurnTime : ratedBurnTime;
            float max100xTime = referenceBurnTime * 2.5f;
            float max100xX = TimeToXPosition(max100xTime, maxTime, plotArea.x, plotArea.width, useLogScaleX);

            if (Event.current.type == EventType.Repaint)
            {
                // Zone 1: Startup (0-5s) - Dark blue (high initial risk)
                GUI.DrawTexture(new Rect(plotArea.x, plotArea.y, startupEndX - plotArea.x, plotArea.height), chartStartupZoneTex);

                // Zone 2: Rated Operation (5s to ratedBurnTime+5) - Green (safe zone)
                GUI.DrawTexture(new Rect(startupEndX, plotArea.y, ratedCushionedX - startupEndX, plotArea.height),
                    chartGreenZoneTex);

                if (hasTestedBurnTime)
                {
                    // Zone 3: Tested Overburn (rated+5 to tested) - Yellow (reduced penalty overburn)
                    GUI.DrawTexture(new Rect(ratedCushionedX, plotArea.y, testedX - ratedCushionedX, plotArea.height),
                        chartYellowZoneTex);

                    // Zone 4: Severe Overburn (tested to 100×) - Red (danger zone)
                    GUI.DrawTexture(new Rect(testedX, plotArea.y, max100xX - testedX, plotArea.height),
                        chartRedZoneTex);

                    // Zone 5: Maximum Overburn (100× to end) - Darker red (nearly linear failure increase)
                    GUI.DrawTexture(new Rect(max100xX, plotArea.y, plotArea.x + plotArea.width - max100xX, plotArea.height),
                        chartDarkRedZoneTex);
                }
                else
                {
                    // Zone 3: Overburn (rated+5 to 100×) - Red (danger zone)
                    GUI.DrawTexture(new Rect(ratedCushionedX, plotArea.y, max100xX - ratedCushionedX, plotArea.height),
                        chartRedZoneTex);

                    // Zone 4: Maximum Overburn (100× to end) - Darker red (nearly linear failure increase)
                    GUI.DrawTexture(new Rect(max100xX, plotArea.y, plotArea.x + plotArea.width - max100xX, plotArea.height),
                        chartDarkRedZoneTex);
                }
            }

            // Draw vertical zone separators (thinner and less prominent)
            if (Event.current.type == EventType.Repaint)
            {
                // Startup zone end (5s) - Blue
                GUI.DrawTexture(new Rect(startupEndX, plotArea.y, 1, plotArea.height), chartMarkerBlueTex);

                // Rated burn time (+ 5s cushion) - Green
                GUI.DrawTexture(new Rect(ratedCushionedX, plotArea.y, 1, plotArea.height), chartMarkerGreenTex);

                // Tested burn time (if present) - Yellow
                if (hasTestedBurnTime)
                {
                    GUI.DrawTexture(new Rect(testedX, plotArea.y, 1, plotArea.height), chartMarkerYellowTex);
                }

                // 100× modifier point (maximum cycle penalty) - Dark Red
                GUI.DrawTexture(new Rect(max100xX, plotArea.y, 1, plotArea.height), chartMarkerDarkRedTex);
            }

            // Now calculate point positions for all curves using the dynamic Y scale
            Vector2[] pointsStart = new Vector2[curvePoints];
            Vector2[] pointsEnd = new Vector2[curvePoints];
            Vector2[] pointsCurrent = hasCurrentData ? new Vector2[curvePoints] : null;

            for (int i = 0; i < curvePoints; i++)
            {
                float t = (i / (float)(curvePoints - 1)) * maxTime;
                float x = TimeToXPosition(t, maxTime, plotArea.x, plotArea.width, useLogScaleX);

                // Start curve (0 data) - orange
                float yStart = FailureProbToYPosition(failureProbsStart[i], yAxisMax, plotArea.y, plotArea.height, useLogScaleY);
                if (float.IsNaN(x) || float.IsNaN(yStart) || float.IsInfinity(x) || float.IsInfinity(yStart))
                {
                    x = plotArea.x;
                    yStart = plotArea.y + plotArea.height;
                }
                pointsStart[i] = new Vector2(x, yStart);

                // End curve (max data) - green
                float yEnd = FailureProbToYPosition(failureProbsEnd[i], yAxisMax, plotArea.y, plotArea.height, useLogScaleY);
                if (float.IsNaN(x) || float.IsNaN(yEnd) || float.IsInfinity(x) || float.IsInfinity(yEnd))
                {
                    x = plotArea.x;
                    yEnd = plotArea.y + plotArea.height;
                }
                pointsEnd[i] = new Vector2(x, yEnd);

                // Current data curve - light blue
                if (hasCurrentData)
                {
                    float yCurrent = FailureProbToYPosition(failureProbsCurrent[i], yAxisMax, plotArea.y, plotArea.height, useLogScaleY);
                    if (float.IsNaN(x) || float.IsNaN(yCurrent) || float.IsInfinity(x) || float.IsInfinity(yCurrent))
                    {
                        x = plotArea.x;
                        yCurrent = plotArea.y + plotArea.height;
                    }
                    pointsCurrent[i] = new Vector2(x, yCurrent);
                }
            }

            // Draw all curves
            if (Event.current.type == EventType.Repaint)
            {
                // Draw start curve (0 data) in orange
                for (int i = 0; i < pointsStart.Length - 1; i++)
                {
                    DrawLine(pointsStart[i], pointsStart[i + 1], chartOrangeLineTex, 2.5f);
                }

                // Draw end curve (max data) in green
                for (int i = 0; i < pointsEnd.Length - 1; i++)
                {
                    DrawLine(pointsEnd[i], pointsEnd[i + 1], chartGreenLineTex, 2.5f);
                }

                // Draw current data curve in light blue
                if (hasCurrentData && pointsCurrent != null)
                {
                    for (int i = 0; i < pointsCurrent.Length - 1; i++)
                    {
                        DrawLine(pointsCurrent[i], pointsCurrent[i + 1], chartBlueLineTex, 2.5f);
                    }
                }
            }

            // X-axis labels (time in minutes)
            var timeStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = Color.grey }, alignment = TextAnchor.UpperCenter };

            if (useLogScaleX)
            {
                // Logarithmic scale labels: show key time points
                float[] logTimes = { 0.1f, 1f, 10f, 60f, 300f, 600f, 1800f, 3600f };
                foreach (float time in logTimes)
                {
                    if (time > maxTime) break;
                    float x = TimeToXPosition(time, maxTime, plotArea.x, plotArea.width, useLogScaleX);
                    string label = time < 60f ? $"{time:F0}s" : $"{time / 60:F0}m";
                    GUI.Label(new Rect(x - 25, plotArea.y + plotArea.height + 2, 50, 20), label, timeStyle);
                }
            }
            else
            {
                // Linear scale labels
                for (int i = 0; i <= 4; i++)
                {
                    float time = (i / 4f) * maxTime;
                    float x = TimeToXPosition(time, maxTime, plotArea.x, plotArea.width, useLogScaleX);
                    GUI.Label(new Rect(x - 25, plotArea.y + plotArea.height + 2, 50, 20), $"{time / 60:F0}m", timeStyle);
                }
            }

            // Chart title
            var titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, normal = { textColor = Color.white }, alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(chartRect.x, chartRect.y + 4, chartWidth, 24), "Failure Probability vs Burn Time", titleStyle);

            // Legend with colored circles
            var legendStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = Color.white }, alignment = TextAnchor.UpperLeft };
            float legendX = plotArea.x + 10;
            float legendY = plotArea.y + 5;

            // Orange circle and line for 0 data
            DrawCircle(new Rect(legendX, legendY + 5, 8, 8), chartOrangeLineTex);
            GUI.DrawTexture(new Rect(legendX + 10, legendY + 7, 15, 3), chartOrangeLineTex);
            GUI.Label(new Rect(legendX + 28, legendY, 80, 18), "0 Data", legendStyle);

            // Blue circle and line for current data (if available)
            if (hasCurrentData)
            {
                DrawCircle(new Rect(legendX, legendY + 23, 8, 8), chartBlueLineTex);
                GUI.DrawTexture(new Rect(legendX + 10, legendY + 25, 15, 3), chartBlueLineTex);
                GUI.Label(new Rect(legendX + 28, legendY + 18, 100, 18), "Current Data", legendStyle);

                // Green circle and line for max data (shifted down)
                DrawCircle(new Rect(legendX, legendY + 41, 8, 8), chartGreenLineTex);
                GUI.DrawTexture(new Rect(legendX + 10, legendY + 43, 15, 3), chartGreenLineTex);
                GUI.Label(new Rect(legendX + 28, legendY + 36, 80, 18), "Max Data", legendStyle);
            }
            else
            {
                // Green circle and line for max data (no shift if no current data)
                DrawCircle(new Rect(legendX, legendY + 23, 8, 8), chartGreenLineTex);
                GUI.DrawTexture(new Rect(legendX + 10, legendY + 25, 15, 3), chartGreenLineTex);
                GUI.Label(new Rect(legendX + 28, legendY + 18, 80, 18), "Max Data", legendStyle);
            }

            // Tooltip handling and hover line
            Vector2 mousePos = Event.current.mousePosition;
            if (plotArea.Contains(mousePos))
            {
                // Draw vertical hover line
                if (Event.current.type == EventType.Repaint)
                {
                    GUI.DrawTexture(new Rect(mousePos.x, plotArea.y, 1, plotArea.height), chartHoverLineTex);
                }

                // Calculate the time at mouse position
                float mouseT = XPositionToTime(mousePos.x, maxTime, plotArea.x, plotArea.width, useLogScaleX);
                mouseT = Mathf.Clamp(mouseT, 0f, maxTime);

                // Determine which zone we're in
                string zoneName = "";
                if (mouseT <= 5f)
                {
                    zoneName = "Engine Startup";
                }
                else if (mouseT <= ratedBurnTime + 5f)
                {
                    zoneName = "Rated Operation";
                }
                else if (hasTestedBurnTime && mouseT <= testedBurnTime)
                {
                    zoneName = "Tested Overburn";
                }
                else if (mouseT <= max100xTime)
                {
                    zoneName = "Severe Overburn";
                }
                else
                {
                    zoneName = "Maximum Overburn";
                }

                // Calculate cycle modifier at this time
                float cycleModifier = cycleCurve.Evaluate(mouseT);

                // Check if hovering near vertical markers for specific marker info
                bool nearStartupMarker = Mathf.Abs(mousePos.x - startupEndX) < 8f;
                bool nearRatedMarker = Mathf.Abs(mousePos.x - ratedCushionedX) < 8f;
                bool nearTestedMarker = hasTestedBurnTime && Mathf.Abs(mousePos.x - testedX) < 8f;
                bool near100xMarker = Mathf.Abs(mousePos.x - max100xX) < 8f;

                string tooltipText = "";
                string valueColor = "#88DDFF"; // Light cyan for values

                if (nearStartupMarker)
                {
                    tooltipText = $"<b><color=#6699CC>Startup Period End</color></b>\n\nFailure risk drops from <color={valueColor}>10×</color> to <color={valueColor}>1×</color> during startup.\nAfter <color={valueColor}>5 seconds</color>, the engine reaches stable operation.";
                }
                else if (nearRatedMarker)
                {
                    float ratedMinutes = ratedBurnTime / 60f;
                    string ratedTimeStr = ratedMinutes >= 1f ? $"{ratedMinutes:F1}m" : $"{ratedBurnTime:F0}s";
                    tooltipText = $"<b><color=#66DD66>Rated Burn Time</color></b>\n\nThis engine is designed to run for <color={valueColor}>{ratedTimeStr}</color>.\nBeyond this point, overburn penalties increase failure risk.";
                }
                else if (nearTestedMarker)
                {
                    float testedMinutes = testedBurnTime / 60f;
                    string testedTimeStr = testedMinutes >= 1f ? $"{testedMinutes:F1}m" : $"{testedBurnTime:F0}s";
                    tooltipText = $"<b><color=#FFCC44>Tested Overburn Limit</color></b>\n\nThis engine was tested to <color={valueColor}>{testedTimeStr}</color> in real life.\nFailure risk reaches <color={valueColor}>{overburnPenalty:F1}×</color> at this point.\nBeyond here, risk increases rapidly toward certain failure.";
                }
                else if (near100xMarker)
                {
                    float max100xMinutes = max100xTime / 60f;
                    string max100xTimeStr = max100xMinutes >= 1f ? $"{max100xMinutes:F1}m" : $"{max100xTime:F0}s";
                    tooltipText = $"<b><color=#CC2222>Maximum Cycle Penalty (100×)</color></b>\n\nAt <color={valueColor}>{max100xTimeStr}</color>, the failure rate multiplier reaches its maximum of <color={valueColor}>100×</color>.\n\nBeyond this point, it doesn't get much worse—failure probability increases nearly linearly with time.";
                }
                else
                {
                    // Calculate failure probabilities at mouse position
                    float mouseFailStart = 0f;
                    float mouseFailEnd = 0f;
                    float mouseFailCurrent = 0f;

                    if (mouseT <= ratedBurnTime)
                    {
                        mouseFailStart = 1f - Mathf.Pow(cycleReliabilityStart, mouseT / ratedBurnTime);
                        mouseFailEnd = 1f - Mathf.Pow(cycleReliabilityEnd, mouseT / ratedBurnTime);
                        if (hasCurrentData)
                            mouseFailCurrent = 1f - Mathf.Pow(cycleReliabilityCurrent, mouseT / ratedBurnTime);
                    }
                    else
                    {
                        float survivalToRatedStart = cycleReliabilityStart;
                        float integratedModifier = IntegrateCycleCurve(cycleCurve, ratedBurnTime, mouseT, 20);
                        float additionalFailRate = baseRateStart * integratedModifier;
                        mouseFailStart = Mathf.Clamp01(1f - (survivalToRatedStart * Mathf.Exp(-additionalFailRate)));

                        float survivalToRatedEnd = cycleReliabilityEnd;
                        additionalFailRate = baseRateEnd * integratedModifier;
                        mouseFailEnd = Mathf.Clamp01(1f - (survivalToRatedEnd * Mathf.Exp(-additionalFailRate)));

                        if (hasCurrentData)
                        {
                            float survivalToRatedCurrent = cycleReliabilityCurrent;
                            additionalFailRate = baseRateCurrent * integratedModifier;
                            mouseFailCurrent = Mathf.Clamp01(1f - (survivalToRatedCurrent * Mathf.Exp(-additionalFailRate)));
                        }
                    }

                    // Apply cluster math to tooltip values
                    if (clusterSize > 1)
                    {
                        mouseFailStart = 1f - Mathf.Pow(1f - mouseFailStart, clusterSize);
                        mouseFailEnd = 1f - Mathf.Pow(1f - mouseFailEnd, clusterSize);
                        if (hasCurrentData)
                            mouseFailCurrent = 1f - Mathf.Pow(1f - mouseFailCurrent, clusterSize);
                    }

                    // Format time string
                    float minutes = Mathf.Floor(mouseT / 60f);
                    float seconds = mouseT % 60f;
                    string timeStr = minutes > 0 ? $"{minutes:F0}m {seconds:F0}s" : $"{seconds:F1}s";

                    // Color code the zone name based on zone type
                    string zoneColor = "";
                    if (mouseT <= 5f)
                        zoneColor = "#6699CC"; // Blue for startup
                    else if (mouseT <= ratedBurnTime + 5f)
                        zoneColor = "#66DD66"; // Green for rated
                    else if (hasTestedBurnTime && mouseT <= testedBurnTime)
                        zoneColor = "#FFCC44"; // Yellow for tested overburn
                    else if (mouseT <= max100xTime)
                        zoneColor = "#FF6666"; // Red for severe overburn
                    else
                        zoneColor = "#CC2222"; // Dark red for maximum overburn

                    // Build tooltip with color-coded values (valueColor already defined above)
                    string orangeColor = "#FF8033"; // Match orange line (0 data)
                    string blueColor = "#7DD9FF";   // Match lighter blue line (current data)
                    string greenColor = "#4DE64D";  // Match green line (max data)

                    tooltipText = $"<b><color={zoneColor}>{zoneName}</color></b>\n\n";
                    tooltipText += $"At <color={valueColor}>{timeStr}</color>, this engine has a:\n\n";
                    tooltipText += $"  <color={orangeColor}>{mouseFailStart * 100f:F2}%</color> chance to fail (0 data)\n";
                    if (hasCurrentData)
                        tooltipText += $"  <color={blueColor}>{mouseFailCurrent * 100f:F2}%</color> chance to fail (current data)\n";
                    tooltipText += $"  <color={greenColor}>{mouseFailEnd * 100f:F2}%</color> chance to fail (max data)\n\n";
                    tooltipText += $"Cycle modifier: <color={valueColor}>{cycleModifier:F2}×</color>";
                }

                // Store tooltip to draw last (after info panel) so it appears on top
                string finalTooltipText = tooltipText;
                Vector2 finalMousePos = mousePos;

                // Draw info panel first
                float ignitionReliabilityStart = 1f;
                float ignitionReliabilityEnd = 1f;
                configNode.TryGetValue("ignitionReliabilityStart", ref ignitionReliabilityStart);
                configNode.TryGetValue("ignitionReliabilityEnd", ref ignitionReliabilityEnd);

                // Calculate current ignition reliability
                float ignitionReliabilityCurrent = hasCurrentData ? Mathf.Lerp(ignitionReliabilityStart, ignitionReliabilityEnd, dataPercentage) : 0f;

                DrawFailureInfoPanel(infoRect, ratedBurnTime, testedBurnTime, hasTestedBurnTime,
                    cycleReliabilityStart, cycleReliabilityEnd, ignitionReliabilityStart, ignitionReliabilityEnd,
                    hasCurrentData, cycleReliabilityCurrent, ignitionReliabilityCurrent, dataPercentage, realDataPercentage);

                // Draw tooltip last so it appears on top of everything
                DrawChartTooltip(finalMousePos, finalTooltipText);
            }
            else
            {
                // No hover, just draw info panel
                float ignitionReliabilityStart = 1f;
                float ignitionReliabilityEnd = 1f;
                configNode.TryGetValue("ignitionReliabilityStart", ref ignitionReliabilityStart);
                configNode.TryGetValue("ignitionReliabilityEnd", ref ignitionReliabilityEnd);

                // Calculate current ignition reliability
                float ignitionReliabilityCurrent = hasCurrentData ? Mathf.Lerp(ignitionReliabilityStart, ignitionReliabilityEnd, dataPercentage) : 0f;

                DrawFailureInfoPanel(infoRect, ratedBurnTime, testedBurnTime, hasTestedBurnTime,
                    cycleReliabilityStart, cycleReliabilityEnd, ignitionReliabilityStart, ignitionReliabilityEnd,
                    hasCurrentData, cycleReliabilityCurrent, ignitionReliabilityCurrent, dataPercentage, realDataPercentage);
            }
        }

        private void DrawFailureInfoPanel(Rect rect, float ratedBurnTime, float testedBurnTime, bool hasTestedBurnTime,
            float cycleReliabilityStart, float cycleReliabilityEnd, float ignitionReliabilityStart, float ignitionReliabilityEnd,
            bool hasCurrentData, float cycleReliabilityCurrent, float ignitionReliabilityCurrent, float dataPercentage, float realDataPercentage)
        {
            // Draw background
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(rect, infoPanelBgTex);
            }

            // Calculate success probabilities (chance to complete the burn)
            float ratedSuccessStart = cycleReliabilityStart * 100f;
            float ratedSuccessEnd = cycleReliabilityEnd * 100f;
            float ignitionSuccessStart = ignitionReliabilityStart * 100f;
            float ignitionSuccessEnd = ignitionReliabilityEnd * 100f;

            // Calculate tested burn success if available
            float testedSuccessStart = 0f;
            float testedSuccessEnd = 0f;
            if (hasTestedBurnTime && testedBurnTime > ratedBurnTime)
            {
                // Use the cycle reliability for the full tested duration
                float testedRatio = testedBurnTime / ratedBurnTime;
                testedSuccessStart = Mathf.Pow(cycleReliabilityStart, testedRatio) * 100f;
                testedSuccessEnd = Mathf.Pow(cycleReliabilityEnd, testedRatio) * 100f;
            }

            // Calculate current data success probabilities
            float ratedSuccessCurrent = 0f;
            float testedSuccessCurrent = 0f;
            float ignitionSuccessCurrent = 0f;
            if (hasCurrentData)
            {
                ratedSuccessCurrent = cycleReliabilityCurrent * 100f;
                ignitionSuccessCurrent = ignitionReliabilityCurrent * 100f;
                if (hasTestedBurnTime && testedBurnTime > ratedBurnTime)
                {
                    float testedRatio = testedBurnTime / ratedBurnTime;
                    testedSuccessCurrent = Mathf.Pow(cycleReliabilityCurrent, testedRatio) * 100f;
                }
            }

            // Apply cluster math: for N engines all succeeding = (singleSuccess)^N
            if (clusterSize > 1)
            {
                // Convert from percentage to decimal, apply power, convert back
                ignitionSuccessStart = Mathf.Pow(ignitionSuccessStart / 100f, clusterSize) * 100f;
                ignitionSuccessEnd = Mathf.Pow(ignitionSuccessEnd / 100f, clusterSize) * 100f;
                ratedSuccessStart = Mathf.Pow(ratedSuccessStart / 100f, clusterSize) * 100f;
                ratedSuccessEnd = Mathf.Pow(ratedSuccessEnd / 100f, clusterSize) * 100f;
                testedSuccessStart = Mathf.Pow(testedSuccessStart / 100f, clusterSize) * 100f;
                testedSuccessEnd = Mathf.Pow(testedSuccessEnd / 100f, clusterSize) * 100f;

                if (hasCurrentData)
                {
                    ignitionSuccessCurrent = Mathf.Pow(ignitionSuccessCurrent / 100f, clusterSize) * 100f;
                    ratedSuccessCurrent = Mathf.Pow(ratedSuccessCurrent / 100f, clusterSize) * 100f;
                    testedSuccessCurrent = Mathf.Pow(testedSuccessCurrent / 100f, clusterSize) * 100f;
                }
            }

            // Style for rich text labels
            var textStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = Color.white },
                wordWrap = true,
                richText = true,
                padding = new RectOffset(8, 8, 2, 2)
            };

            var headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f) },
                wordWrap = true,
                richText = true,
                padding = new RectOffset(8, 8, 0, 4) // No top padding to align with chart title
            };

            var sectionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.5f, 0.2f) }, // Orange for 0 data
                wordWrap = true,
                richText = true,
                padding = new RectOffset(8, 8, 4, 2)
            };

            // Color codes matching the chart lines
            string orangeColor = "#FF8033"; // 0 data
            string blueColor = "#7DD9FF";   // Current data (lighter blue)
            string greenColor = "#4DE64D";  // Max data
            string valueColor = "#88DDFF";  // Time values

            // Start at same vertical position as chart title for alignment
            float yPos = rect.y + 4;

            // Title
            GUI.Label(new Rect(rect.x, yPos, rect.width, 20), "Engine Reliability:", headerStyle);
            yPos += 24;

            // === 0 DATA SECTION (Orange) ===
            sectionStyle.normal.textColor = new Color(1f, 0.5f, 0.2f);
            GUI.Label(new Rect(rect.x, yPos, rect.width, 18), "At 0 Data:", sectionStyle);
            yPos += 20;

            // Build narrative text for 0 data
            string engineText = clusterSize > 1 ? $"A cluster of <color={valueColor}>{clusterSize}</color> engines" : "This engine";
            string text0Data = $"{engineText} has a <color={orangeColor}>{ignitionSuccessStart:F1}%</color> chance for all to ignite, ";
            text0Data += $"then a <color={orangeColor}>{ratedSuccessStart:F1}%</color> chance for all to burn for <color={valueColor}>{ratedBurnTime:F0}s</color> (rated)";
            if (hasTestedBurnTime)
                text0Data += $", and a <color={orangeColor}>{testedSuccessStart:F1}%</color> chance for all to burn to <color={valueColor}>{testedBurnTime:F0}s</color> (tested)";
            text0Data += ".";

            float height0 = textStyle.CalcHeight(new GUIContent(text0Data), rect.width);
            GUI.Label(new Rect(rect.x, yPos, rect.width, height0), text0Data, textStyle);
            yPos += height0 + 8;

            // === CURRENT DATA SECTION (Blue) - only if available ===
            if (hasCurrentData)
            {
                sectionStyle.normal.textColor = new Color(0.49f, 0.85f, 1.0f); // Lighter blue to match line
                GUI.Label(new Rect(rect.x, yPos, rect.width, 18), $"At Current Data ({dataPercentage * 100f:F0}%):", sectionStyle);
                yPos += 20;

                // Build narrative text for current data
                string textCurrentData = $"{engineText} has a <color={blueColor}>{ignitionSuccessCurrent:F1}%</color> chance for all to ignite, ";
                textCurrentData += $"then a <color={blueColor}>{ratedSuccessCurrent:F1}%</color> chance for all to burn for <color={valueColor}>{ratedBurnTime:F0}s</color> (rated)";
                if (hasTestedBurnTime)
                    textCurrentData += $", and a <color={blueColor}>{testedSuccessCurrent:F1}%</color> chance for all to burn to <color={valueColor}>{testedBurnTime:F0}s</color> (tested)";
                textCurrentData += ".";

                float heightCurrent = textStyle.CalcHeight(new GUIContent(textCurrentData), rect.width);
                GUI.Label(new Rect(rect.x, yPos, rect.width, heightCurrent), textCurrentData, textStyle);
                yPos += heightCurrent + 8;
            }

            // === MAX DATA SECTION (Green) ===
            sectionStyle.normal.textColor = new Color(0.3f, 0.9f, 0.3f);
            GUI.Label(new Rect(rect.x, yPos, rect.width, 18), "At Max Data:", sectionStyle);
            yPos += 20;

            // Build narrative text for max data
            string textMaxData = $"{engineText} has a <color={greenColor}>{ignitionSuccessEnd:F1}%</color> chance for all to ignite, ";
            textMaxData += $"then a <color={greenColor}>{ratedSuccessEnd:F1}%</color> chance for all to burn for <color={valueColor}>{ratedBurnTime:F0}s</color> (rated)";
            if (hasTestedBurnTime)
                textMaxData += $", and a <color={greenColor}>{testedSuccessEnd:F1}%</color> chance for all to burn to <color={valueColor}>{testedBurnTime:F0}s</color> (tested)";
            textMaxData += ".";

            float heightMax = textStyle.CalcHeight(new GUIContent(textMaxData), rect.width);
            GUI.Label(new Rect(rect.x, yPos, rect.width, heightMax), textMaxData, textStyle);
            yPos += heightMax + 16;

            // === SIMULATION CONTROLS ===
            bool hasRealData = realDataPercentage >= 0f && realDataPercentage <= 1f;

            var controlStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.8f, 0.8f, 0.8f) },
                padding = new RectOffset(8, 8, 2, 2)
            };

            var sliderStyle = GUI.skin.horizontalSlider;
            var thumbStyle = GUI.skin.horizontalSliderThumb;
            var buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 11 };
            var inputStyle = new GUIStyle(GUI.skin.textField) { fontSize = 11, alignment = TextAnchor.MiddleCenter };

            // Separator line
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(new Rect(rect.x + 8, yPos, rect.width - 16, 1), chartSeparatorTex);
            }
            yPos += 6;

            // === BUTTONS ROW: Log X, Log Y, Reset (3 side by side) ===
            float btnWidth = (rect.width - 24) / 3f;
            float btnSpacing = 4f;

            // X-axis log scale toggle
            string toggleLabelX = useLogScaleX ? "X: Lin" : "X: Log";
            if (GUI.Button(new Rect(rect.x + 8, yPos, btnWidth, 20), toggleLabelX, buttonStyle))
            {
                useLogScaleX = !useLogScaleX;
            }

            // Y-axis log scale toggle
            string toggleLabelY = useLogScaleY ? "Y: Lin" : "Y: Log";
            if (GUI.Button(new Rect(rect.x + 8 + btnWidth + btnSpacing, yPos, btnWidth, 20), toggleLabelY, buttonStyle))
            {
                useLogScaleY = !useLogScaleY;
            }

            // Reset button
            string resetButtonText = hasRealData ? $"{realDataPercentage * 100f:F0}%" : "0%";
            if (GUI.Button(new Rect(rect.x + 8 + (btnWidth + btnSpacing) * 2, yPos, btnWidth, 20), resetButtonText, buttonStyle))
            {
                if (hasRealData)
                {
                    simulatedDataPercentage = realDataPercentage;
                    dataPercentageInput = $"{realDataPercentage * 100f:F0}";
                    useSimulatedData = false;
                }
                else
                {
                    simulatedDataPercentage = 0f;
                    dataPercentageInput = "0";
                    useSimulatedData = true;
                }
                clusterSize = 1;
                clusterSizeInput = "1";
            }

            yPos += 26;

            // === TWO SLIDERS SIDE BY SIDE ===
            float halfWidth = (rect.width - 24) / 2f;
            float leftX = rect.x;
            float rightX = rect.x + halfWidth + 8;

            // LEFT: DATA PERCENTAGE
            GUI.Label(new Rect(leftX, yPos, halfWidth, 16), "Data %", controlStyle);
            GUI.Label(new Rect(rightX, yPos, halfWidth, 16), "Cluster", controlStyle);
            yPos += 16;

            // Initialize simulated value to real data if not yet simulating
            if (!useSimulatedData)
            {
                simulatedDataPercentage = hasRealData ? realDataPercentage : 0f;
                dataPercentageInput = $"{simulatedDataPercentage * 100f:F0}";
            }

            // Data % slider
            simulatedDataPercentage = GUI.HorizontalSlider(new Rect(leftX + 8, yPos, halfWidth - 60, 16),
                simulatedDataPercentage * 100f, 0f, 100f, sliderStyle, thumbStyle) / 100f;

            // Mark as simulated if user moved slider away from real data
            if (hasRealData && Mathf.Abs(simulatedDataPercentage - realDataPercentage) > 0.001f)
            {
                useSimulatedData = true;
            }
            else if (!hasRealData && simulatedDataPercentage > 0.001f)
            {
                useSimulatedData = true;
            }

            // Data % input field
            dataPercentageInput = $"{simulatedDataPercentage * 100f:F0}";
            GUI.SetNextControlName("dataPercentInput");
            string newDataInput = GUI.TextField(new Rect(leftX + halfWidth - 45, yPos - 2, 40, 20),
                dataPercentageInput, 5, inputStyle);

            if (newDataInput != dataPercentageInput)
            {
                dataPercentageInput = newDataInput;
                if (GUI.GetNameOfFocusedControl() == "dataPercentInput" && float.TryParse(dataPercentageInput, out float inputDataPercent))
                {
                    inputDataPercent = Mathf.Clamp(inputDataPercent, 0f, 100f);
                    simulatedDataPercentage = inputDataPercent / 100f;
                    useSimulatedData = true;
                }
            }

            // RIGHT: CLUSTER SIZE
            // Cluster slider
            clusterSize = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(rightX + 8, yPos, halfWidth - 60, 16),
                clusterSize, 1f, 20f, sliderStyle, thumbStyle));

            // Cluster input field
            clusterSizeInput = clusterSize.ToString();
            GUI.SetNextControlName("clusterSizeInput");
            string newClusterInput = GUI.TextField(new Rect(rightX + halfWidth - 45, yPos - 2, 40, 20),
                clusterSizeInput, 3, inputStyle);

            if (newClusterInput != clusterSizeInput)
            {
                clusterSizeInput = newClusterInput;
                if (GUI.GetNameOfFocusedControl() == "clusterSizeInput" && int.TryParse(clusterSizeInput, out int inputCluster))
                {
                    inputCluster = Mathf.Clamp(inputCluster, 1, 20);
                    clusterSize = inputCluster;
                    clusterSizeInput = clusterSize.ToString();
                }
            }
        }

        private void DrawLine(Vector2 start, Vector2 end, Texture2D texture, float width)
        {
            if (texture == null)
                return;

            Vector2 diff = end - start;
            float angle = Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg;
            float length = diff.magnitude;

            Matrix4x4 matrixBackup = GUI.matrix;
            try
            {
                GUIUtility.RotateAroundPivot(angle, start);
                GUI.DrawTexture(new Rect(start.x, start.y - width / 2, length, width), texture);
            }
            finally
            {
                GUI.matrix = matrixBackup;
            }
        }

        private void DrawCircle(Rect rect, Texture2D texture)
        {
            if (texture == null || Event.current.type != EventType.Repaint)
                return;

            // Draw circle by drawing a filled square with rounded appearance
            // For simplicity, we'll draw concentric squares that approximate a circle
            float centerX = rect.x + rect.width / 2f;
            float centerY = rect.y + rect.height / 2f;
            float radius = rect.width / 2f;

            for (float r = radius; r > 0; r -= 0.5f)
            {
                float size = r * 2f;
                GUI.DrawTexture(new Rect(centerX - r, centerY - r, size, size), texture);
            }
        }

        private float RoundToNiceNumber(float value, bool roundUp)
        {
            if (value <= 0f) return 0f;

            // Find the order of magnitude
            float exponent = Mathf.Floor(Mathf.Log10(value));
            float fraction = value / Mathf.Pow(10f, exponent);

            // Round to 1, 2, or 5 times the magnitude
            float niceFraction;
            if (roundUp)
            {
                if (fraction <= 1f) niceFraction = 1f;
                else if (fraction <= 2f) niceFraction = 2f;
                else if (fraction <= 5f) niceFraction = 5f;
                else niceFraction = 10f;
            }
            else
            {
                if (fraction < 1.5f) niceFraction = 1f;
                else if (fraction < 3.5f) niceFraction = 2f;
                else if (fraction < 7.5f) niceFraction = 5f;
                else niceFraction = 10f;
            }

            return niceFraction * Mathf.Pow(10f, exponent);
        }

        private void DrawChartTooltip(Vector2 mousePos, string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            var tooltipStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 15,
                normal = { textColor = Color.white, background = chartTooltipBgTex },
                padding = new RectOffset(8, 8, 6, 6),
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                richText = true
            };

            GUIContent content = new GUIContent(text);
            Vector2 size = tooltipStyle.CalcSize(content);

            // Position tooltip offset from mouse, but keep it within screen bounds
            float tooltipX = mousePos.x + 15;
            float tooltipY = mousePos.y + 15;

            // Adjust if tooltip would go off-screen
            if (tooltipX + size.x > Screen.width)
                tooltipX = mousePos.x - size.x - 5;
            if (tooltipY + size.y > Screen.height)
                tooltipY = mousePos.y - size.y - 5;

            Rect tooltipRect = new Rect(tooltipX, tooltipY, size.x, size.y);
            GUI.Box(tooltipRect, content, tooltipStyle);
        }

        virtual protected void DrawConfigSelectors(IEnumerable<ConfigNode> availableConfigNodes)
        {
            DrawConfigTable(BuildConfigRows());
        }


        protected void DrawTechLevelSelector()
        {
            // NK Tech Level
            if (techLevel != -1)
            {
                GUILayout.BeginHorizontal();

                GUILayout.Label($"{Localizer.GetStringByTag("#RF_Engine_TechLevel")}: "); // Tech Level
                string minusStr = "X";
                bool canMinus = false;
                if (TechLevel.CanTL(config, techNodes, engineType, techLevel - 1) && techLevel > minTechLevel)
                {
                    minusStr = "-";
                    canMinus = true;
                }
                if (GUILayout.Button(minusStr) && canMinus)
                {
                    techLevel--;
                    SetConfiguration();
                    UpdateSymmetryCounterparts();
                    MarkWindowDirty();
                }
                GUILayout.Label(techLevel.ToString());
                string plusStr = "X";
                bool canPlus = false;
                bool canBuy = false;
                string tlName = Utilities.GetPartName(part) + configuration;
                double tlIncrMult = (double)(techLevel + 1 - origTechLevel);
                if (TechLevel.CanTL(config, techNodes, engineType, techLevel + 1) && techLevel < maxTechLevel)
                {
                    if (UnlockedTL(tlName, techLevel + 1))
                    {
                        plusStr = "+";
                        canPlus = true;
                    }
                    else
                    {
                        double cost = EntryCostManager.Instance.TLEntryCost(tlName) * tlIncrMult;
                        double sciCost = EntryCostManager.Instance.TLSciEntryCost(tlName) * tlIncrMult;
                        bool autobuy = true;
                        plusStr = string.Empty;
                        if (cost > 0d)
                        {
                            plusStr += cost.ToString("N0") + "√";
                            autobuy = false;
                            canBuy = true;
                        }
                        if (sciCost > 0d)
                        {
                            if (cost > 0d)
                                plusStr += "/";
                            autobuy = false;
                            canBuy = true;
                            plusStr += sciCost.ToString("N1") + "s";
                        }
                        if (autobuy)
                        {
                            // auto-upgrade
                            EntryCostManager.Instance.SetTLUnlocked(tlName, techLevel + 1);
                            plusStr = "+";
                            canPlus = true;
                            canBuy = false;
                        }
                    }
                }
                if (GUILayout.Button(plusStr) && (canPlus || canBuy))
                {
                    if (!canBuy || EntryCostManager.Instance.PurchaseTL(tlName, techLevel + 1, tlIncrMult))
                    {
                        techLevel++;
                        SetConfiguration();
                        UpdateSymmetryCounterparts();
                        MarkWindowDirty();
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        private void EngineManagerGUI(int WindowID)
        {
            GUILayout.Space(6); // Breathing room at top

            GUILayout.BeginHorizontal();
            GUILayout.Label(EditorDescription);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(compactView ? "Full View" : "Compact View", GUILayout.Width(100)))
            {
                compactView = !compactView;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6); // Space before table
            DrawConfigSelectors(FilteredDisplayConfigs(false));

            // Draw failure probability chart for current config
            if (config != null)
            {
                GUILayout.Space(8);
                DrawFailureProbabilityChart(config, guiWindowRect.width - 10, 360);
            }

            DrawTechLevelSelector();

            // Only use negative space if no chart (chart needs the room)
            if (config == null || !config.HasValue("cycleReliabilityStart"))
                GUILayout.Space(-80); // Remove all bottom padding - window ends right at table
            else
                GUILayout.Space(8); // Add space after chart

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
        protected static List<PartModule> GetSpecifiedModules(Part p, string eID, int mIdx, string eType, bool weakType)
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

            if (UnlockedConfig(node, part)) return true;

            techToResolve = config.GetValue("techRequired");
            if (!CanConfig(node))
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
