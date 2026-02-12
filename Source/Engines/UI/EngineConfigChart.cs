using System;
using UnityEngine;

namespace RealFuels
{
    /// <summary>
    /// Handles all chart rendering for engine configuration failure probability visualization.
    /// Displays survival probability curves based on TestFlight data.
    /// </summary>
    public class EngineConfigChart
    {
        private readonly ModuleEngineConfigsBase _module;
        private readonly EngineConfigTextures _textures;

        // Chart state
        private bool _useLogScaleX = false;
        private bool _useLogScaleY = false;

        // Simulation state
        private bool _useSimulatedData = false;
        private float _simulatedDataValue = 0f;
        private int _clusterSize = 1;
        private string _clusterSizeInput = "1";
        private string _dataValueInput = "0";
        private string _sliderTimeInput = "100.0";
        private bool _includeIgnition = false;

        // Public properties for external access
        public bool UseLogScaleX { get => _useLogScaleX; set => _useLogScaleX = value; }
        public bool UseLogScaleY { get => _useLogScaleY; set => _useLogScaleY = value; }
        public bool UseSimulatedData { get => _useSimulatedData; set => _useSimulatedData = value; }
        public float SimulatedDataValue { get => _simulatedDataValue; set => _simulatedDataValue = value; }
        public int ClusterSize { get => _clusterSize; set => _clusterSize = value; }
        public string ClusterSizeInput { get => _clusterSizeInput; set => _clusterSizeInput = value; }
        public string DataValueInput { get => _dataValueInput; set => _dataValueInput = value; }
        public string SliderTimeInput { get => _sliderTimeInput; set => _sliderTimeInput = value; }
        public bool IncludeIgnition { get => _includeIgnition; set => _includeIgnition = value; }

        public EngineConfigChart(ModuleEngineConfigsBase module)
        {
            _module = module;
            _textures = EngineConfigTextures.Instance;
        }

        /// <summary>
        /// Draws the failure probability chart and info panel side by side.
        /// </summary>
        public void Draw(ConfigNode configNode, float width, float height, ref float sliderTime)
        {
            _textures.EnsureInitialized();
            EngineConfigStyles.Initialize();

            // Values are copied to CONFIG level by ModuleManager patch
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
            if (ratedContinuousBurnTime < ratedBurnTime * 0.9f)
            {
                // Display error message for dual burn time configs
                GUIStyle redCenteredStyle = new GUIStyle(GUI.skin.label)
                {
                    normal = { textColor = Color.red },
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true
                };

                GUILayout.BeginVertical(GUILayout.Width(width), GUILayout.Height(height));
                GUILayout.FlexibleSpace();
                GUILayout.Label("Dual burn time configurations (continuous/cumulative)\nare not supported for reliability charts", redCenteredStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndVertical();
                return;
            }

            // Read testedBurnTime to match TestFlight's exact behavior
            float testedBurnTime = 0f;
            bool hasTestedBurnTime = configNode.TryGetValue("testedBurnTime", ref testedBurnTime) && testedBurnTime > ratedBurnTime;

            // Split the area: chart on left (58%), info on right (42%)
            float chartWidth = width * 0.58f;
            float infoWidth = width * 0.42f;

            float overburnPenalty = 2.0f;
            configNode.TryGetValue("overburnPenalty", ref overburnPenalty);

            // Build the actual TestFlight cycle curve
            FloatCurve cycleCurve = ChartMath.BuildTestFlightCycleCurve(ratedBurnTime, testedBurnTime, overburnPenalty, hasTestedBurnTime);

            // Main container
            Rect containerRect = GUILayoutUtility.GetRect(width, height);

            // Chart area (left side)
            const float padding = 38f;
            float plotWidth = chartWidth - padding * 2;
            float plotHeight = height - padding * 2;

            float maxTime = hasTestedBurnTime ? testedBurnTime * 3.5f : ratedBurnTime * 3.5f;

            Rect chartRect = new Rect(containerRect.x, containerRect.y, chartWidth, height);
            Rect plotArea = new Rect(chartRect.x + padding, chartRect.y + padding, plotWidth, plotHeight);

            // Info panel area (right side)
            Rect infoRect = new Rect(containerRect.x + chartWidth, containerRect.y, infoWidth, height);

            // Get ignition reliability values
            float ignitionReliabilityStart = 1f;
            float ignitionReliabilityEnd = 1f;
            configNode.TryGetValue("ignitionReliabilityStart", ref ignitionReliabilityStart);
            configNode.TryGetValue("ignitionReliabilityEnd", ref ignitionReliabilityEnd);

            // Calculate survival curves
            var curveData = ChartMath.CalculateSurvivalCurves(
                cycleReliabilityStart, cycleReliabilityEnd,
                ratedBurnTime, cycleCurve, maxTime, _clusterSize);

            // Get current data
            float realCurrentData = TestFlightWrapper.GetCurrentFlightData(_module.part);
            float realMaxData = TestFlightWrapper.GetMaximumData(_module.part);
            float currentDataValue = _useSimulatedData ? _simulatedDataValue : realCurrentData;
            float maxDataValue = realMaxData > 0f ? realMaxData : 10000f;
            float dataPercentage = (maxDataValue > 0f) ? Mathf.Clamp01(currentDataValue / maxDataValue) : 0f;
            bool hasCurrentData = (_useSimulatedData && currentDataValue >= 0f) || (realCurrentData >= 0f && realMaxData > 0f);

            float cycleReliabilityCurrent = 0f;
            ChartMath.SurvivalCurveData currentCurveData = default;
            float ignitionReliabilityCurrent = 0f;

            if (hasCurrentData)
            {
                cycleReliabilityCurrent = ChartMath.EvaluateReliabilityAtData(currentDataValue, cycleReliabilityStart, cycleReliabilityEnd);
                ignitionReliabilityCurrent = ChartMath.EvaluateReliabilityAtData(currentDataValue, ignitionReliabilityStart, ignitionReliabilityEnd);
                currentCurveData = ChartMath.CalculateSurvivalCurve(
                    cycleReliabilityCurrent, ratedBurnTime, cycleCurve, maxTime, _clusterSize);
            }

            // If including ignition, apply ignition reliability to all curves (vertical scaling)
            if (_includeIgnition)
            {
                // Apply cluster math to ignition probabilities
                float clusteredIgnitionStart = _clusterSize > 1 ? Mathf.Pow(ignitionReliabilityStart, _clusterSize) : ignitionReliabilityStart;
                float clusteredIgnitionEnd = _clusterSize > 1 ? Mathf.Pow(ignitionReliabilityEnd, _clusterSize) : ignitionReliabilityEnd;
                float clusteredIgnitionCurrent = hasCurrentData && _clusterSize > 1 ? Mathf.Pow(ignitionReliabilityCurrent, _clusterSize) : ignitionReliabilityCurrent;

                // Apply clustered ignition to start curve
                for (int i = 0; i < curveData.SurvivalProbs.Length; i++)
                {
                    curveData.SurvivalProbs[i] *= clusteredIgnitionStart;
                }

                // Apply clustered ignition to end curve
                for (int i = 0; i < curveData.SurvivalProbsEnd.Length; i++)
                {
                    curveData.SurvivalProbsEnd[i] *= clusteredIgnitionEnd;
                }

                // Apply clustered ignition to current curve if available
                if (hasCurrentData)
                {
                    for (int i = 0; i < currentCurveData.SurvivalProbs.Length; i++)
                    {
                        currentCurveData.SurvivalProbs[i] *= clusteredIgnitionCurrent;
                    }
                }

                // Update min survival prob
                curveData.MinSurvivalProb = Mathf.Min(
                    curveData.SurvivalProbs[curveData.SurvivalProbs.Length - 1],
                    curveData.SurvivalProbsEnd[curveData.SurvivalProbsEnd.Length - 1]
                );
            }

            // Draw chart
            DrawChartBackground(chartRect);
            DrawGrid(plotArea, curveData.MinSurvivalProb, maxTime);
            DrawCurves(plotArea, curveData, currentCurveData, hasCurrentData, maxTime, curveData.MinSurvivalProb);
            DrawSliderTimeLine(plotArea, sliderTime, maxTime);
            DrawAxisLabels(chartRect, plotArea, maxTime, curveData.MinSurvivalProb);
            DrawLegend(plotArea, hasCurrentData);

            // Draw info panel
            DrawInfoPanel(infoRect, configNode, ratedBurnTime, testedBurnTime, hasTestedBurnTime,
                cycleReliabilityStart, cycleReliabilityEnd, hasCurrentData, cycleReliabilityCurrent,
                dataPercentage, currentDataValue, maxDataValue, realCurrentData, realMaxData,
                cycleCurve, ref sliderTime, maxTime);

            // Sync back slider time input for consistency
            _sliderTimeInput = $"{sliderTime:F1}";
        }

        #region Chart Background & Zones

        private void DrawChartBackground(Rect chartRect)
        {
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(chartRect, _textures.ChartBg);
            }

            // Chart title
            GUI.Label(new Rect(chartRect.x, chartRect.y + 4, chartRect.width, 24),
                "Survival Probability vs Burn Time", EngineConfigStyles.ChartTitle);
        }


        #endregion

        #region Grid & Axes

        private void DrawGrid(Rect plotArea, float yAxisMin, float maxTime)
        {
            GUIStyle labelStyle = EngineConfigStyles.GridLabel;

            if (_useLogScaleY)
            {
                float[] logValues = { 0.0001f, 0.001f, 0.01f, 0.1f, 1f };
                foreach (float survivalProb in logValues)
                {
                    if (survivalProb < yAxisMin) continue;
                    float y = ChartMath.SurvivalProbToYPosition(survivalProb, yAxisMin, plotArea.y, plotArea.height, _useLogScaleY);
                    DrawGridLine(plotArea.x, y, plotArea.width);
                    DrawYAxisLabel(plotArea.x, y, survivalProb);
                }
            }
            else
            {
                for (int i = 0; i <= 10; i++)
                {
                    bool isMajor = (i % 2 == 0);
                    float survivalProb = yAxisMin + (i / 10f) * (1f - yAxisMin);
                    float y = ChartMath.SurvivalProbToYPosition(survivalProb, yAxisMin, plotArea.y, plotArea.height, _useLogScaleY);

                    DrawGridLine(plotArea.x, y, plotArea.width, isMajor);
                    if (isMajor) DrawYAxisLabel(plotArea.x, y, survivalProb);
                }
            }
        }

        private void DrawGridLine(float x, float y, float width, bool major = true)
        {
            if (Event.current.type != EventType.Repaint) return;
            Rect lineRect = new Rect(x, y, width, 1);
            GUI.DrawTexture(lineRect, major ? _textures.ChartGridMajor : _textures.ChartGridMinor);
        }

        private void DrawYAxisLabel(float x, float y, float survivalProb)
        {
            float labelValue = survivalProb * 100f;
            string label = labelValue < 1f ? $"{labelValue:F2}%" :
                          (labelValue < 10f ? $"{labelValue:F1}%" : $"{labelValue:F0}%");
            GUI.Label(new Rect(x - 35, y - 10, 30, 20), label, EngineConfigStyles.GridLabel);
        }

        private void DrawAxisLabels(Rect chartRect, Rect plotArea, float maxTime, float yAxisMin)
        {
            // X-axis labels
            GUIStyle timeStyle = EngineConfigStyles.TimeLabel;

            if (_useLogScaleX)
            {
                float[] logTimes = { 0.1f, 1f, 10f, 60f, 300f, 600f, 1800f, 3600f };
                foreach (float time in logTimes)
                {
                    if (time > maxTime) break;
                    float x = ChartMath.TimeToXPosition(time, maxTime, plotArea.x, plotArea.width, _useLogScaleX);
                    GUI.Label(new Rect(x - 25, plotArea.y + plotArea.height + 2, 50, 20),
                        ChartMath.FormatTime(time), timeStyle);
                }
            }
            else
            {
                for (int i = 0; i <= 4; i++)
                {
                    float time = (i / 4f) * maxTime;
                    float x = ChartMath.TimeToXPosition(time, maxTime, plotArea.x, plotArea.width, _useLogScaleX);
                    GUI.Label(new Rect(x - 25, plotArea.y + plotArea.height + 2, 50, 20),
                        ChartMath.FormatTime(time), timeStyle);
                }
            }
        }

        #endregion

        #region Curve Drawing

        private void DrawCurves(Rect plotArea, ChartMath.SurvivalCurveData startCurve,
            ChartMath.SurvivalCurveData currentCurve, bool hasCurrentData,
            float maxTime, float yAxisMin)
        {
            if (Event.current.type != EventType.Repaint) return;

            // Convert to screen positions
            Vector2[] pointsStart = ConvertToScreenPoints(startCurve.SurvivalProbs, plotArea, maxTime, yAxisMin);
            Vector2[] pointsEnd = ConvertToScreenPoints(startCurve.SurvivalProbsEnd, plotArea, maxTime, yAxisMin);
            Vector2[] pointsCurrent = hasCurrentData ? ConvertToScreenPoints(currentCurve.SurvivalProbs, plotArea, maxTime, yAxisMin) : null;

            // Draw curves
            DrawCurveLine(pointsStart, _textures.ChartOrangeLine, plotArea);
            DrawCurveLine(pointsEnd, _textures.ChartGreenLine, plotArea);
            if (hasCurrentData && pointsCurrent != null)
                DrawCurveLine(pointsCurrent, _textures.ChartBlueLine, plotArea);
        }

        private Vector2[] ConvertToScreenPoints(float[] survivalProbs, Rect plotArea, float maxTime, float yAxisMin)
        {
            int count = survivalProbs.Length;
            Vector2[] points = new Vector2[count];

            for (int i = 0; i < count; i++)
            {
                float t = (i / (float)(count - 1)) * maxTime;
                float x = ChartMath.TimeToXPosition(t, maxTime, plotArea.x, plotArea.width, _useLogScaleX);
                float y = ChartMath.SurvivalProbToYPosition(survivalProbs[i], yAxisMin, plotArea.y, plotArea.height, _useLogScaleY);

                if (float.IsNaN(x) || float.IsNaN(y) || float.IsInfinity(x) || float.IsInfinity(y))
                {
                    x = plotArea.x;
                    y = plotArea.y + plotArea.height;
                }
                points[i] = new Vector2(x, y);
            }

            return points;
        }

        private void DrawCurveLine(Vector2[] points, Texture2D texture, Rect plotArea)
        {
            float plotAreaRight = plotArea.x + plotArea.width;

            for (int i = 0; i < points.Length - 1; i++)
            {
                // Skip segments outside plot area
                if (points[i].x > plotAreaRight && points[i + 1].x > plotAreaRight) continue;
                if (points[i].x < plotArea.x && points[i + 1].x < plotArea.x) continue;

                ChartMath.DrawLine(points[i], points[i + 1], texture, 2.5f);
            }
        }

        #endregion

        #region Slider Time Line

        private void DrawSliderTimeLine(Rect plotArea, float sliderTime, float maxTime)
        {
            if (Event.current.type != EventType.Repaint) return;

            float x = ChartMath.TimeToXPosition(sliderTime, maxTime, plotArea.x, plotArea.width, _useLogScaleX);

            // Clamp to plot area
            if (x < plotArea.x || x > plotArea.x + plotArea.width) return;

            // Draw white vertical line
            Color whiteTransparent = new Color(1f, 1f, 1f, 0.8f);
            Texture2D whiteLine = MakeTex(2, 2, whiteTransparent);
            GUI.DrawTexture(new Rect(x - 1f, plotArea.y, 2f, plotArea.height), whiteLine);
        }

        private Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++)
                pix[i] = col;
            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }

        #endregion

        #region Legend

        private void DrawLegend(Rect plotArea, bool hasCurrentData)
        {
            GUIStyle legendStyle = EngineConfigStyles.Legend;
            float legendWidth = 110f;
            float legendX = plotArea.x + plotArea.width - legendWidth;
            float legendY = plotArea.y + 5;

            // Orange line for 0 data
            GUI.DrawTexture(new Rect(legendX, legendY + 7, 15, 3), _textures.ChartOrangeLine);
            GUI.Label(new Rect(legendX + 18, legendY, 80, 18), "0 Data", legendStyle);

            if (hasCurrentData)
            {
                GUI.DrawTexture(new Rect(legendX, legendY + 25, 15, 3), _textures.ChartBlueLine);
                GUI.Label(new Rect(legendX + 18, legendY + 18, 100, 18), "Current Data", legendStyle);

                GUI.DrawTexture(new Rect(legendX, legendY + 43, 15, 3), _textures.ChartGreenLine);
                GUI.Label(new Rect(legendX + 18, legendY + 36, 80, 18), "Max Data", legendStyle);
            }
            else
            {
                GUI.DrawTexture(new Rect(legendX, legendY + 25, 15, 3), _textures.ChartGreenLine);
                GUI.Label(new Rect(legendX + 18, legendY + 18, 80, 18), "Max Data", legendStyle);
            }
        }

        #endregion


        #region Info Panel Integration

        private void DrawInfoPanel(Rect rect, ConfigNode configNode, float ratedBurnTime, float testedBurnTime, bool hasTestedBurnTime,
            float cycleReliabilityStart, float cycleReliabilityEnd, bool hasCurrentData, float cycleReliabilityCurrent,
            float dataPercentage, float currentDataValue, float maxDataValue, float realCurrentData, float realMaxData,
            FloatCurve cycleCurve, ref float sliderTime, float maxTime)
        {
            float ignitionReliabilityStart = 1f;
            float ignitionReliabilityEnd = 1f;
            configNode.TryGetValue("ignitionReliabilityStart", ref ignitionReliabilityStart);
            configNode.TryGetValue("ignitionReliabilityEnd", ref ignitionReliabilityEnd);

            float ignitionReliabilityCurrent = hasCurrentData ?
                ChartMath.EvaluateReliabilityAtData(currentDataValue, ignitionReliabilityStart, ignitionReliabilityEnd) : 0f;

            var infoPanel = new EngineConfigInfoPanel(_module);
            infoPanel.Draw(rect, ratedBurnTime, testedBurnTime, hasTestedBurnTime,
                cycleReliabilityStart, cycleReliabilityEnd, ignitionReliabilityStart, ignitionReliabilityEnd,
                hasCurrentData, cycleReliabilityCurrent, ignitionReliabilityCurrent, dataPercentage,
                currentDataValue, maxDataValue, realCurrentData, realMaxData,
                ref _useSimulatedData, ref _simulatedDataValue, ref _clusterSize,
                ref _clusterSizeInput, ref _dataValueInput, ref sliderTime, ref _sliderTimeInput,
                ref _includeIgnition, cycleCurve, maxTime);
        }

        #endregion
    }
}
