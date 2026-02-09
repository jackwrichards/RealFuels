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
            ref string clusterSizeInput, ref string dataValueInput)
        {
            // Draw background
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(rect, _textures.InfoPanelBg);
            }

            float yPos = rect.y + 4;

            // Draw reliability section
            yPos = DrawReliabilitySection(rect, yPos, ratedBurnTime, testedBurnTime, hasTestedBurnTime,
                cycleReliabilityStart, cycleReliabilityEnd, ignitionReliabilityStart, ignitionReliabilityEnd,
                hasCurrentData, cycleReliabilityCurrent, ignitionReliabilityCurrent,
                dataPercentage, currentDataValue, maxDataValue, clusterSize);

            // Separator
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(new Rect(rect.x + 8, yPos, rect.width - 16, 1), _textures.ChartSeparator);
            }
            yPos += 10;

            // Side-by-side: Data Gains (left) and Controls (right)
            yPos = DrawSideBySideSection(rect, yPos, ratedBurnTime, maxDataValue, realCurrentData, realMaxData,
                ref useSimulatedData, ref simulatedDataValue, ref clusterSize, ref clusterSizeInput, ref dataValueInput);

            // Bottom separator
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(new Rect(rect.x + 8, yPos, rect.width - 16, 1), _textures.ChartSeparator);
            }
            yPos += 10;

            // Failure rate summary
            DrawFailureRateSummary(rect, yPos, ratedBurnTime, currentDataValue, cycleReliabilityStart,
                cycleReliabilityEnd, clusterSize);
        }

        #region Reliability Section

        private float DrawReliabilitySection(Rect rect, float yPos,
            float ratedBurnTime, float testedBurnTime, bool hasTestedBurnTime,
            float cycleReliabilityStart, float cycleReliabilityEnd,
            float ignitionReliabilityStart, float ignitionReliabilityEnd,
            bool hasCurrentData, float cycleReliabilityCurrent, float ignitionReliabilityCurrent,
            float dataPercentage, float currentDataValue, float maxDataValue, int clusterSize)
        {
            // Calculate success probabilities
            float ratedSuccessStart = cycleReliabilityStart * 100f;
            float ratedSuccessEnd = cycleReliabilityEnd * 100f;
            float ignitionSuccessStart = ignitionReliabilityStart * 100f;
            float ignitionSuccessEnd = ignitionReliabilityEnd * 100f;

            float testedSuccessStart = 0f;
            float testedSuccessEnd = 0f;
            if (hasTestedBurnTime && testedBurnTime > ratedBurnTime)
            {
                float testedRatio = testedBurnTime / ratedBurnTime;
                testedSuccessStart = Mathf.Pow(cycleReliabilityStart, testedRatio) * 100f;
                testedSuccessEnd = Mathf.Pow(cycleReliabilityEnd, testedRatio) * 100f;
            }

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

            // Apply cluster math
            if (clusterSize > 1)
            {
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

            // Color codes
            string orangeColor = "#FF8033";
            string blueColor = "#7DD9FF";
            string greenColor = "#4DE64D";
            string valueColor = "#E6D68A";

            // Header
            string headerText = $"At <color={orangeColor}>Starting</color>";
            if (hasCurrentData)
            {
                string dataLabel = maxDataValue > 0f ? $"{currentDataValue:F0} du" : $"{dataPercentage * 100f:F0}%";
                headerText += $" / <color={blueColor}>Current ({dataLabel})</color>";
            }
            headerText += $" / <color={greenColor}>Max</color>:";

            GUIStyle sectionStyle = EngineConfigStyles.InfoSection;
            sectionStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(rect.x, yPos, rect.width, 20), headerText, sectionStyle);
            yPos += 24;

            // Build narrative
            string engineText = clusterSize > 1 ? $"A cluster of <color={valueColor}>{clusterSize}</color> engines" : "This engine";
            string forAll = clusterSize > 1 ? " for all" : "";
            string combinedText = $"{engineText} has a ";

            // Ignition success rates
            if (hasCurrentData)
                combinedText += $"<size=17><color={orangeColor}>{ignitionSuccessStart:F1}%</color></size> / <size=17><color={blueColor}>{ignitionSuccessCurrent:F1}%</color></size> / <size=17><color={greenColor}>{ignitionSuccessEnd:F1}%</color></size>";
            else
                combinedText += $"<size=17><color={orangeColor}>{ignitionSuccessStart:F1}%</color></size> / <size=17><color={greenColor}>{ignitionSuccessEnd:F1}%</color></size>";

            combinedText += $" chance{forAll} to ignite, then a ";

            // Rated burn success rates
            if (hasCurrentData)
                combinedText += $"<size=17><color={orangeColor}>{ratedSuccessStart:F1}%</color></size> / <size=17><color={blueColor}>{ratedSuccessCurrent:F1}%</color></size> / <size=17><color={greenColor}>{ratedSuccessEnd:F1}%</color></size>";
            else
                combinedText += $"<size=17><color={orangeColor}>{ratedSuccessStart:F1}%</color></size> / <size=17><color={greenColor}>{ratedSuccessEnd:F1}%</color></size>";

            combinedText += $" chance{forAll} to burn for <color={valueColor}>{ChartMath.FormatTime(ratedBurnTime)}</color> (rated)";

            // Tested burn success rates
            if (hasTestedBurnTime)
            {
                combinedText += ", and a ";
                if (hasCurrentData)
                    combinedText += $"<size=17><color={orangeColor}>{testedSuccessStart:F1}%</color></size> / <size=17><color={blueColor}>{testedSuccessCurrent:F1}%</color></size> / <size=17><color={greenColor}>{testedSuccessEnd:F1}%</color></size>";
                else
                    combinedText += $"<size=17><color={orangeColor}>{testedSuccessStart:F1}%</color></size> / <size=17><color={greenColor}>{testedSuccessEnd:F1}%</color></size>";

                combinedText += $" chance{forAll} to burn to <color={valueColor}>{ChartMath.FormatTime(testedBurnTime)}</color> (tested)";
            }
            combinedText += ".";

            GUIStyle textStyle = EngineConfigStyles.InfoText;
            float combinedHeight = textStyle.CalcHeight(new GUIContent(combinedText), rect.width);
            GUI.Label(new Rect(rect.x, yPos, rect.width, combinedHeight), combinedText, textStyle);
            yPos += combinedHeight + 12;

            return yPos;
        }

        #endregion

        #region Side-by-Side Section

        private float DrawSideBySideSection(Rect rect, float yPos, float ratedBurnTime, float maxDataValue,
            float realCurrentData, float realMaxData,
            ref bool useSimulatedData, ref float simulatedDataValue, ref int clusterSize,
            ref string clusterSizeInput, ref string dataValueInput)
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
                maxDataValue, realCurrentData, realMaxData,
                ref useSimulatedData, ref simulatedDataValue, ref clusterSize, ref clusterSizeInput, ref dataValueInput);

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
                string failText = $"  ({failurePercents[i]:F0}%) {failureTypes[i]} <color={purpleColor}>+{failureDu[i]}</color> du";
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

        private float DrawSimulationControls(float x, float width, float yPos, float maxDataValue,
            float realCurrentData, float realMaxData,
            ref bool useSimulatedData, ref float simulatedDataValue, ref int clusterSize,
            ref string clusterSizeInput, ref string dataValueInput)
        {
            bool hasRealData = realCurrentData >= 0f && realMaxData > 0f;

            GUIStyle sectionStyle = EngineConfigStyles.InfoSection;
            sectionStyle.normal.textColor = new Color(0.8f, 0.7f, 1.0f);
            GUI.Label(new Rect(x, yPos, width, 20), "Simulate:", sectionStyle);
            yPos += 24;

            GUIStyle buttonStyle = EngineConfigStyles.CompactButton;
            var inputStyle = new GUIStyle(GUI.skin.textField) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUIStyle controlStyle = EngineConfigStyles.Control;

            // Reset button
            float resetBtnWidth = width - 16;
            string resetButtonText = hasRealData ? $"Set to Current du ({realCurrentData:F0})" : "Set to Current du (0)";
            if (GUI.Button(new Rect(x + 8, yPos, resetBtnWidth, 20), resetButtonText, buttonStyle))
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
            float btnWidth = width - 16;
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

        #region Failure Rate Summary

        private void DrawFailureRateSummary(Rect rect, float yPos, float ratedBurnTime, float currentDataValue,
            float cycleReliabilityStart, float cycleReliabilityEnd, int clusterSize)
        {
            float cycleReliabilityAtCurrentData = ChartMath.EvaluateReliabilityAtData(currentDataValue,
                cycleReliabilityStart, cycleReliabilityEnd);

            if (clusterSize > 1)
            {
                cycleReliabilityAtCurrentData = Mathf.Pow(cycleReliabilityAtCurrentData, clusterSize);
            }

            float failureRate = 1f - cycleReliabilityAtCurrentData;
            float oneInX = failureRate > 0.0001f ? (1f / failureRate) : 9999f;

            string valueColor = "#E6D68A";
            string failureRateColor = "#FF6666";
            string failureText = $"With <color={valueColor}>{currentDataValue:F0}</color> du, 1 in <color={failureRateColor}>{oneInX:F1}</color> rated burns will fail (<color={valueColor}>{ChartMath.FormatTime(ratedBurnTime)}</color>)";

            GUIStyle failureRateStyle = EngineConfigStyles.FailureRate;
            float failureTextHeight = failureRateStyle.CalcHeight(new GUIContent(failureText), rect.width);
            GUI.Label(new Rect(rect.x, yPos, rect.width, failureTextHeight), failureText, failureRateStyle);
        }

        #endregion
    }
}
