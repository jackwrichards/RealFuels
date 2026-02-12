using System;
using UnityEngine;

namespace RealFuels
{
    /// <summary>
    /// Handles the info panel display showing reliability stats, data gains, and simulation controls.
    /// </summary>
    public class EngineConfigInfoPanel
    {
        private readonly ModuleEngineConfigsBase _module;
        private readonly EngineConfigTextures _textures;

        public EngineConfigInfoPanel(ModuleEngineConfigsBase module)
        {
            _module = module;
            _textures = EngineConfigTextures.Instance;
        }

        public void Draw(Rect rect, float ratedBurnTime, float testedBurnTime, bool hasTestedBurnTime,
            float cycleReliabilityStart, float cycleReliabilityEnd, float ignitionReliabilityStart, float ignitionReliabilityEnd,
            bool hasCurrentData, float cycleReliabilityCurrent, float ignitionReliabilityCurrent, float dataPercentage,
            float currentDataValue, float maxDataValue, float realCurrentData, float realMaxData,
            ref bool useSimulatedData, ref float simulatedDataValue, ref int clusterSize,
            ref string clusterSizeInput, ref string dataValueInput, ref float sliderTime, ref string sliderTimeInput,
            ref bool includeIgnition, FloatCurve cycleCurve, float maxGraphTime)
        {
            // Draw background
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(rect, _textures.InfoPanelBg);
            }

            float yPos = rect.y + 4;

            // Draw reliability section (burn survival - three sections with optional ignition text)
            yPos = DrawReliabilitySection(rect, yPos, ratedBurnTime,
                cycleReliabilityStart, cycleReliabilityEnd, cycleReliabilityCurrent,
                ignitionReliabilityStart, ignitionReliabilityEnd, ignitionReliabilityCurrent,
                hasCurrentData, cycleCurve, clusterSize, sliderTime, includeIgnition);

            // Separator
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(new Rect(rect.x + 8, yPos, rect.width - 16, 1), _textures.ChartSeparator);
            }
            yPos += 10;

            // Side-by-side: Data Gains (left) and Controls (right)
            yPos = DrawSideBySideSection(rect, yPos, ratedBurnTime, maxGraphTime, maxDataValue, realCurrentData, realMaxData,
                ref useSimulatedData, ref simulatedDataValue, ref clusterSize, ref clusterSizeInput, ref dataValueInput,
                ref sliderTime, ref sliderTimeInput, ref includeIgnition);
        }

        #region Reliability Section

        private float DrawReliabilitySection(Rect rect, float yPos,
            float ratedBurnTime,
            float cycleReliabilityStart, float cycleReliabilityEnd, float cycleReliabilityCurrent,
            float ignitionReliabilityStart, float ignitionReliabilityEnd, float ignitionReliabilityCurrent,
            bool hasCurrentData, FloatCurve cycleCurve,
            int clusterSize, float sliderTime, bool includeIgnition)
        {
            // Color codes
            string orangeColor = "#FF8033";
            string blueColor = "#7DD9FF";
            string greenColor = "#4DE64D";

            // Calculate base rates
            float baseRateStart = -Mathf.Log(cycleReliabilityStart) / ratedBurnTime;
            float baseRateEnd = -Mathf.Log(cycleReliabilityEnd) / ratedBurnTime;
            float baseRateCurrent = hasCurrentData ? -Mathf.Log(cycleReliabilityCurrent) / ratedBurnTime : 0f;

            // Calculate burn survival probabilities at slider time
            float surviveStart = ChartMath.CalculateSurvivalProbAtTime(sliderTime, ratedBurnTime, cycleReliabilityStart, baseRateStart, cycleCurve);
            float surviveEnd = ChartMath.CalculateSurvivalProbAtTime(sliderTime, ratedBurnTime, cycleReliabilityEnd, baseRateEnd, cycleCurve);
            float surviveCurrent = hasCurrentData ? ChartMath.CalculateSurvivalProbAtTime(sliderTime, ratedBurnTime, cycleReliabilityCurrent, baseRateCurrent, cycleCurve) : 0f;

            // If including ignition, multiply by ignition reliability
            if (includeIgnition)
            {
                surviveStart *= ignitionReliabilityStart;
                surviveEnd *= ignitionReliabilityEnd;
                if (hasCurrentData) surviveCurrent *= ignitionReliabilityCurrent;
            }

            // Apply cluster math
            if (clusterSize > 1)
            {
                surviveStart = Mathf.Pow(surviveStart, clusterSize);
                surviveEnd = Mathf.Pow(surviveEnd, clusterSize);
                if (hasCurrentData) surviveCurrent = Mathf.Pow(surviveCurrent, clusterSize);
            }

            // Layout: three sections side-by-side
            float sectionHeight = 125f;  // Keep constant height to prevent window jumping
            float totalWidth = rect.width - 16f;
            float numSections = hasCurrentData ? 3f : 2f;
            float sectionWidth = totalWidth / numSections;
            float startX = rect.x + 8f;

            float currentX = startX;

            // Calculate ignition probabilities with cluster math
            float igniteStart = clusterSize > 1 ? Mathf.Pow(ignitionReliabilityStart, clusterSize) : ignitionReliabilityStart;
            float igniteEnd = clusterSize > 1 ? Mathf.Pow(ignitionReliabilityEnd, clusterSize) : ignitionReliabilityEnd;
            float igniteCurrent = hasCurrentData ? (clusterSize > 1 ? Mathf.Pow(ignitionReliabilityCurrent, clusterSize) : ignitionReliabilityCurrent) : 0f;

            // Draw Starting DU section
            DrawSurvivalSection(currentX, yPos, sectionWidth, sectionHeight, "Starting DU", orangeColor, surviveStart, sliderTime, clusterSize, igniteStart, includeIgnition);
            currentX += sectionWidth;

            // Draw Current DU section (if applicable)
            if (hasCurrentData)
            {
                DrawSurvivalSection(currentX, yPos, sectionWidth, sectionHeight, "Current DU", blueColor, surviveCurrent, sliderTime, clusterSize, igniteCurrent, includeIgnition);
                currentX += sectionWidth;
            }

            // Draw Max DU section
            DrawSurvivalSection(currentX, yPos, sectionWidth, sectionHeight, "Max DU", greenColor, surviveEnd, sliderTime, clusterSize, igniteEnd, includeIgnition);

            yPos += sectionHeight + 12;

            return yPos;
        }

        private void DrawSurvivalSection(float x, float y, float width, float height, string title, string color, float survivalProb, float time, int clusterSize, float ignitionProb, bool includeIgnition)
        {
            // Header with colored text (no background)
            GUIStyle headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperCenter,
                richText = true,
                normal = { textColor = Color.white }
            };

            string headerText = $"<color={color}>{title}</color>";
            GUI.Label(new Rect(x, y, width, 24), headerText, headerStyle);

            // Survival probability
            float survivalPercent = survivalProb * 100f;
            string survivalText = $"<size=24><b>{survivalPercent:F2}%</b></size>";
            GUIStyle survivalStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                alignment = TextAnchor.MiddleCenter,
                richText = true,
                normal = { textColor = Color.white }
            };
            GUI.Label(new Rect(x, y + 26, width, 28), survivalText, survivalStyle);

            // "1 in X" text
            float failureRate = 1f - survivalProb;
            float oneInX = failureRate > 0.0001f ? (1f / failureRate) : 9999f;
            string entityText = clusterSize > 1 ? $"cluster of {clusterSize}" : "burn";
            string failText = $"1 in <color=#FF6666>{oneInX:F1}</color> {entityText}s will fail to reach {ChartMath.FormatTime(time)}";
            GUIStyle failStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.UpperCenter,
                richText = true,
                wordWrap = true,
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f) }
            };
            GUI.Label(new Rect(x + 4, y + 54, width - 8, 50), failText, failStyle);

            // Small ignition probability text (only when not including ignition)
            if (!includeIgnition)
            {
                float ignitionPercent = ignitionProb * 100f;
                string ignitionText = $"Ignition: {ignitionPercent:F2}%";
                GUIStyle ignitionStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13,
                    alignment = TextAnchor.UpperCenter,
                    normal = { textColor = new Color(0.9f, 0.9f, 0.9f) }
                };
                GUI.Label(new Rect(x + 4, y + 92, width - 8, 18), ignitionText, ignitionStyle);
            }
        }

        #endregion

        #region Side-by-Side Section

        private float DrawSideBySideSection(Rect rect, float yPos, float ratedBurnTime, float maxGraphTime, float maxDataValue,
            float realCurrentData, float realMaxData,
            ref bool useSimulatedData, ref float simulatedDataValue, ref int clusterSize,
            ref string clusterSizeInput, ref string dataValueInput, ref float sliderTime, ref string sliderTimeInput, ref bool includeIgnition)
        {
            float columnStartY = yPos;
            float leftColumnWidth = rect.width * 0.5f;
            float rightColumnWidth = rect.width * 0.5f;
            float leftColumnX = rect.x;
            float rightColumnX = rect.x + leftColumnWidth;

            // Draw left column: Data Gains
            float leftColumnEndY = DrawDataGainsSection(leftColumnX, leftColumnWidth, columnStartY, ratedBurnTime);

            // Draw right column: Simulation Controls
            float rightColumnEndY = DrawSimulationControls(rightColumnX, rightColumnWidth, columnStartY,
                maxGraphTime, maxDataValue, realCurrentData, realMaxData,
                ref useSimulatedData, ref simulatedDataValue, ref clusterSize, ref clusterSizeInput, ref dataValueInput,
                ref sliderTime, ref sliderTimeInput, ref includeIgnition);

            // Draw vertical separator
            if (Event.current.type == EventType.Repaint)
            {
                float separatorX = rect.x + leftColumnWidth;
                float separatorHeight = Mathf.Max(leftColumnEndY, rightColumnEndY) - columnStartY;
                GUI.DrawTexture(new Rect(separatorX, columnStartY, 1, separatorHeight), _textures.ChartSeparator);
            }

            return Mathf.Max(leftColumnEndY, rightColumnEndY) + 8;
        }

        private float DrawDataGainsSection(float x, float width, float yPos, float ratedBurnTime)
        {
            string purpleColor = "#CCB3FF";

            float ratedContinuousBurnTime = ratedBurnTime;
            float dataRate = 640f / ratedContinuousBurnTime;

            // Section header
            GUIStyle sectionStyle = EngineConfigStyles.InfoSection;
            sectionStyle.normal.textColor = new Color(0.8f, 0.7f, 1.0f);
            GUI.Label(new Rect(x, yPos, width, 20), "How To Gain Data:", sectionStyle);
            yPos += 24;

            GUIStyle bulletStyle = EngineConfigStyles.Bullet;
            GUIStyle indentedBulletStyle = EngineConfigStyles.IndentedBullet;
            GUIStyle footerStyle = EngineConfigStyles.Footer;
            float bulletHeight = 18;

            // Failures section
            GUI.Label(new Rect(x, yPos, width, bulletHeight), "An engine can fail in 4 ways:", bulletStyle);
            yPos += bulletHeight;

            string[] failureTypes = { "Shutdown", "Perf. Loss", "Reduced Thrust", "Explode" };
            int[] failureDu = { 1000, 800, 700, 1000 };
            float[] failurePercents = { 55.2f, 27.6f, 13.8f, 3.4f };

            for (int i = 0; i < failureTypes.Length; i++)
            {
                string failText = $" ({failurePercents[i]:F0}%) {failureTypes[i]} <color={purpleColor}>+{failureDu[i]}</color> du";
                GUI.Label(new Rect(x, yPos, width, bulletHeight), failText, indentedBulletStyle);
                yPos += bulletHeight;
            }

            yPos += 4;

            // Running gains
            string runningText = $"Running gains <color={purpleColor}>{dataRate:F1}</color> du/s";
            GUI.Label(new Rect(x, yPos, width, bulletHeight), runningText, bulletStyle);
            yPos += bulletHeight;

            // Ignition failure
            string ignitionText = $"Ignition Fail <color={purpleColor}>+1000</color> du";
            GUI.Label(new Rect(x, yPos, width, bulletHeight), ignitionText, bulletStyle);
            yPos += bulletHeight + 8;

            // Footer
            string footerText = "(no more than 1000 du per flight)";
            GUI.Label(new Rect(x, yPos, width, bulletHeight), footerText, footerStyle);
            yPos += bulletHeight;

            return yPos;
        }

        private float DrawSimulationControls(float x, float width, float yPos, float maxGraphTime, float maxDataValue,
            float realCurrentData, float realMaxData,
            ref bool useSimulatedData, ref float simulatedDataValue, ref int clusterSize,
            ref string clusterSizeInput, ref string dataValueInput, ref float sliderTime, ref string sliderTimeInput, ref bool includeIgnition)
        {
            bool hasRealData = realCurrentData >= 0f && realMaxData > 0f;

            GUIStyle sectionStyle = EngineConfigStyles.InfoSection;
            sectionStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(x, yPos, width, 20), "Simulate:", sectionStyle);
            yPos += 24;

            GUIStyle buttonStyle = EngineConfigStyles.CompactButton;
            var inputStyle = new GUIStyle(GUI.skin.textField) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUIStyle controlStyle = EngineConfigStyles.Control;

            // Common width for all controls
            float btnWidth = width - 16;

            // Burn Time slider (first control) - max matches graph range
            GUI.Label(new Rect(x + 8, yPos, btnWidth, 16), "Burn Time (s)", controlStyle);
            yPos += 16;

            float maxSliderTime = maxGraphTime;
            sliderTime = GUI.HorizontalSlider(new Rect(x + 8, yPos, btnWidth - 50, 16),
                sliderTime, 0f, maxSliderTime, GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);

            sliderTimeInput = $"{sliderTime:F1}";
            GUI.SetNextControlName("sliderTimeInput");
            string newTimeInput = GUI.TextField(new Rect(x + btnWidth - 35, yPos - 2, 40, 20),
                sliderTimeInput, 8, inputStyle);

            if (newTimeInput != sliderTimeInput)
            {
                sliderTimeInput = newTimeInput;
                if (GUI.GetNameOfFocusedControl() == "sliderTimeInput" && float.TryParse(sliderTimeInput, out float inputTime))
                {
                    inputTime = Mathf.Clamp(inputTime, 0f, maxSliderTime);
                    sliderTime = inputTime;
                }
            }
            yPos += 24;

            // Include Ignition checkbox
            GUIStyle checkboxStyle = new GUIStyle(GUI.skin.toggle)
            {
                fontSize = 12,
                normal = { textColor = Color.white }
            };
            includeIgnition = GUI.Toggle(new Rect(x + 8, yPos, btnWidth, 20), includeIgnition, " Include Ignition", checkboxStyle);
            yPos += 24;

            // Reset button
            string resetButtonText = hasRealData ? $"Set to Current du ({realCurrentData:F0})" : "Set to Current du (0)";
            if (GUI.Button(new Rect(x + 8, yPos, btnWidth, 20), resetButtonText, buttonStyle))
            {
                if (hasRealData)
                {
                    simulatedDataValue = realCurrentData;
                    dataValueInput = $"{realCurrentData:F0}";
                    useSimulatedData = false;
                }
                else
                {
                    simulatedDataValue = 0f;
                    dataValueInput = "0";
                    useSimulatedData = true;
                }
                clusterSize = 1;
                clusterSizeInput = "1";
            }
            yPos += 24;

            // Data slider
            GUI.Label(new Rect(x + 8, yPos, btnWidth, 16), "Data (du)", controlStyle);
            yPos += 16;

            if (!useSimulatedData)
            {
                simulatedDataValue = hasRealData ? realCurrentData : 0f;
                dataValueInput = $"{simulatedDataValue:F0}";
            }

            simulatedDataValue = GUI.HorizontalSlider(new Rect(x + 8, yPos, btnWidth - 50, 16),
                simulatedDataValue, 0f, maxDataValue, GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb);

            if (hasRealData && Mathf.Abs(simulatedDataValue - realCurrentData) > 0.1f)
                useSimulatedData = true;
            else if (!hasRealData && simulatedDataValue > 0.1f)
                useSimulatedData = true;

            dataValueInput = $"{simulatedDataValue:F0}";
            GUI.SetNextControlName("dataValueInput");
            string newDataInput = GUI.TextField(new Rect(x + btnWidth - 35, yPos - 2, 40, 20),
                dataValueInput, 6, inputStyle);

            if (newDataInput != dataValueInput)
            {
                dataValueInput = newDataInput;
                if (GUI.GetNameOfFocusedControl() == "dataValueInput" && float.TryParse(dataValueInput, out float inputDataValue))
                {
                    inputDataValue = Mathf.Clamp(inputDataValue, 0f, maxDataValue);
                    simulatedDataValue = inputDataValue;
                    useSimulatedData = true;
                }
            }
            yPos += 24;

            // Cluster slider
            GUI.Label(new Rect(x + 8, yPos, btnWidth, 16), "Cluster", controlStyle);
            yPos += 16;

            clusterSize = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x + 8, yPos, btnWidth - 50, 16),
                clusterSize, 1f, 100f, GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb));

            clusterSizeInput = clusterSize.ToString();
            GUI.SetNextControlName("clusterSizeInput");
            string newClusterInput = GUI.TextField(new Rect(x + btnWidth - 35, yPos - 2, 40, 20),
                clusterSizeInput, 3, inputStyle);

            if (newClusterInput != clusterSizeInput)
            {
                clusterSizeInput = newClusterInput;
                if (GUI.GetNameOfFocusedControl() == "clusterSizeInput" && int.TryParse(clusterSizeInput, out int inputCluster))
                {
                    inputCluster = Mathf.Clamp(inputCluster, 1, 100);
                    clusterSize = inputCluster;
                    clusterSizeInput = clusterSize.ToString();
                }
            }
            yPos += 24;

            return yPos;
        }

        #endregion
    }
}
