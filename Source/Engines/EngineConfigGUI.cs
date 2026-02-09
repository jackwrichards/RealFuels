using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using KSP.Localization;

namespace RealFuels
{
    /// <summary>
    /// Handles all GUI rendering for ModuleEngineConfigs.
    /// Manages the configuration selector window, column visibility, tooltips, and user interaction.
    /// </summary>
    public class EngineConfigGUI
    {
        private readonly ModuleEngineConfigsBase _module;
        private readonly EngineConfigTechLevels _techLevels;
        private readonly EngineConfigTextures _textures;
        private EngineConfigChart _chart;

        // GUI state
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
        private bool useLogScaleX = false;
        private bool useLogScaleY = false;

        // Column visibility customization
        private bool showColumnMenu = false;
        private static Rect columnMenuRect = new Rect(100, 100, 280, 650);
        private static bool[] columnsVisibleFull = new bool[18];
        private static bool[] columnsVisibleCompact = new bool[18];
        private static bool columnVisibilityInitialized = false;

        // Simulation controls
        private bool useSimulatedData = false;
        private float simulatedDataValue = 0f;
        private int clusterSize = 1;
        private string clusterSizeInput = "1";
        private string dataValueInput = "0";

        private const int ConfigRowHeight = 22;
        private const int ConfigMaxVisibleRows = 16;
        private float[] ConfigColumnWidths = new float[18];

        private int toolTipWidth => EditorLogic.fetch.editorScreen == EditorScreen.Parts ? 220 : 300;
        private int toolTipHeight => (int)Styles.styleEditorTooltip.CalcHeight(new GUIContent(myToolTip), toolTipWidth);

        public EngineConfigGUI(ModuleEngineConfigsBase module)
        {
            _module = module;
            _techLevels = new EngineConfigTechLevels(module);
            _textures = EngineConfigTextures.Instance;
        }

        private EngineConfigChart Chart
        {
            get
            {
                if (_chart == null)
                {
                    _chart = new EngineConfigChart(_module);
                    _chart.UseLogScaleX = useLogScaleX;
                    _chart.UseLogScaleY = useLogScaleY;
                    _chart.UseSimulatedData = useSimulatedData;
                    _chart.SimulatedDataValue = simulatedDataValue;
                    _chart.ClusterSize = clusterSize;
                }
                return _chart;
            }
        }

        #region Main GUI Entry Point

        public void OnGUI()
        {
            // GUI rendering code will go here
            // This is a placeholder - will be filled with actual implementation
        }

        #endregion

        #region Helper Methods

        internal void MarkWindowDirty()
        {
            lastPartId = 0;
        }

        private void EditorLock()
        {
            if (!editorLocked)
            {
                EditorLogic.fetch.Lock(true, true, true, _module.GetInstanceID().ToString());
                editorLocked = true;
            }
        }

        private void EditorUnlock()
        {
            if (editorLocked)
            {
                EditorLogic.fetch.Unlock(_module.GetInstanceID().ToString());
                editorLocked = false;
            }
        }

        #endregion
    }
}
