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

        // Public properties for external access
        public bool UseLogScaleX { get => _useLogScaleX; set => _useLogScaleX = value; }
        public bool UseLogScaleY { get => _useLogScaleY; set => _useLogScaleY = value; }
        public bool UseSimulatedData { get => _useSimulatedData; set => _useSimulatedData = value; }
        public float SimulatedDataValue { get => _simulatedDataValue; set => _simulatedDataValue = value; }
        public int ClusterSize { get => _clusterSize; set => _clusterSize = value; }
        public string ClusterSizeInput { get => _clusterSizeInput; set => _clusterSizeInput = value; }
        public string DataValueInput { get => _dataValueInput; set => _dataValueInput = value; }

        public EngineConfigChart(ModuleEngineConfigsBase module)
        {
            _module = module;
            _textures = EngineConfigTextures.Instance;
        }

        /// <summary>
        /// Draws the failure probability chart and info panel side by side.
        /// </summary>
        public void Draw(ConfigNode configNode, float width, float height)
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
            if (ratedContinuousBurnTime < ratedBurnTime * 0.9f) return;

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

            if (hasCurrentData)
            {
                cycleReliabilityCurrent = ChartMath.EvaluateReliabilityAtData(currentDataValue, cycleReliabilityStart, cycleReliabilityEnd);
                currentCurveData = ChartMath.CalculateSurvivalCurve(
                    cycleReliabilityCurrent, ratedBurnTime, cycleCurve, maxTime, _clusterSize);
            }

            // Draw chart
            DrawChartBackground(chartRect);
            DrawChartZones(plotArea, ratedBurnTime, testedBurnTime, hasTestedBurnTime, maxTime, overburnPenalty);
            DrawGrid(plotArea, curveData.MinSurvivalProb, maxTime);
            DrawCurves(plotArea, curveData, currentCurveData, hasCurrentData, maxTime, curveData.MinSurvivalProb);
            DrawAxisLabels(chartRect, plotArea, maxTime, curveData.MinSurvivalProb);
            DrawLegend(plotArea, hasCurrentData);
            DrawChartTooltip(plotArea, curveData, currentCurveData, hasCurrentData,
                cycleReliabilityStart, cycleReliabilityCurrent, cycleReliabilityEnd,
                ratedBurnTime, testedBurnTime, hasTestedBurnTime, maxTime, overburnPenalty, cycleCurve);

            // Draw info panel
            DrawInfoPanel(infoRect, configNode, ratedBurnTime, testedBurnTime, hasTestedBurnTime,
                cycleReliabilityStart, cycleReliabilityEnd, hasCurrentData, cycleReliabilityCurrent,
                dataPercentage, currentDataValue, maxDataValue, realCurrentData, realMaxData);
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

        private void DrawChartZones(Rect plotArea, float ratedBurnTime, float testedBurnTime,
            bool hasTestedBurnTime, float maxTime, float overburnPenalty)
        {
            // Zone boundaries
            float startupEndX = ChartMath.TimeToXPosition(5f, maxTime, plotArea.x, plotArea.width, _useLogScaleX);
            float ratedCushionedX = ChartMath.TimeToXPosition(ratedBurnTime + 5f, maxTime, plotArea.x, plotArea.width, _useLogScaleX);
            float testedX = hasTestedBurnTime ? ChartMath.TimeToXPosition(testedBurnTime, maxTime, plotArea.x, plotArea.width, _useLogScaleX) : 0f;

            float referenceBurnTime = hasTestedBurnTime ? testedBurnTime : ratedBurnTime;
            float max100xTime = referenceBurnTime * 2.5f;
            float max100xX = ChartMath.TimeToXPosition(max100xTime, maxTime, plotArea.x, plotArea.width, _useLogScaleX);

            // Clamp to plot area
            float plotAreaRight = plotArea.x + plotArea.width;
            startupEndX = Mathf.Clamp(startupEndX, plotArea.x, plotAreaRight);
            ratedCushionedX = Mathf.Clamp(ratedCushionedX, plotArea.x, plotAreaRight);
            testedX = Mathf.Clamp(testedX, plotArea.x, plotAreaRight);
            max100xX = Mathf.Clamp(max100xX, plotArea.x, plotAreaRight);

            if (Event.current.type != EventType.Repaint) return;

            // Draw zone backgrounds
            DrawZoneRect(plotArea.x, startupEndX, plotArea, _textures.ChartStartupZone);
            DrawZoneRect(startupEndX, ratedCushionedX, plotArea, _textures.ChartGreenZone);

            if (hasTestedBurnTime)
            {
                DrawZoneRect(ratedCushionedX, testedX, plotArea, _textures.ChartYellowZone);
                DrawZoneRect(testedX, max100xX, plotArea, _textures.ChartRedZone);
                DrawZoneRect(max100xX, plotAreaRight, plotArea, _textures.ChartDarkRedZone);
            }
            else
            {
                DrawZoneRect(ratedCushionedX, max100xX, plotArea, _textures.ChartRedZone);
                DrawZoneRect(max100xX, plotAreaRight, plotArea, _textures.ChartDarkRedZone);
            }

            // Draw zone markers
            Vector2 mousePos = Event.current.mousePosition;
            bool mouseInPlot = plotArea.Contains(mousePos);

            DrawZoneMarker(startupEndX, plotArea, _textures.ChartMarkerBlue, mouseInPlot, mousePos);
            DrawZoneMarker(ratedCushionedX, plotArea, _textures.ChartMarkerGreen, mouseInPlot, mousePos);
            if (hasTestedBurnTime) DrawZoneMarker(testedX, plotArea, _textures.ChartMarkerYellow, mouseInPlot, mousePos);
            DrawZoneMarker(max100xX, plotArea, _textures.ChartMarkerDarkRed, mouseInPlot, mousePos);
        }

        private void DrawZoneRect(float x1, float x2, Rect plotArea, Texture2D texture)
        {
            float width = Mathf.Max(0, x2 - x1);
            if (width > 0)
                GUI.DrawTexture(new Rect(x1, plotArea.y, width, plotArea.height), texture);
        }

        private void DrawZoneMarker(float x, Rect plotArea, Texture2D texture, bool mouseInPlot, Vector2 mousePos)
        {
            if (x < plotArea.x || x > plotArea.x + plotArea.width) return;

            bool nearMarker = mouseInPlot && Mathf.Abs(mousePos.x - x) < 8f;
            float lineWidth = nearMarker ? 4f : 1f;
            GUI.DrawTexture(new Rect(x - lineWidth / 2f, plotArea.y, lineWidth, plotArea.height), texture);
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

        #region Legend

        private void DrawLegend(Rect plotArea, bool hasCurrentData)
        {
            GUIStyle legendStyle = EngineConfigStyles.Legend;
            float legendWidth = 110f;
            float legendX = plotArea.x + plotArea.width - legendWidth;
            float legendY = plotArea.y + 5;

            // Orange circle and line for 0 data
            ChartMath.DrawCircle(new Rect(legendX, legendY + 5, 8, 8), _textures.ChartOrangeLine);
            GUI.DrawTexture(new Rect(legendX + 10, legendY + 7, 15, 3), _textures.ChartOrangeLine);
            GUI.Label(new Rect(legendX + 28, legendY, 80, 18), "0 Data", legendStyle);

            if (hasCurrentData)
            {
                ChartMath.DrawCircle(new Rect(legendX, legendY + 23, 8, 8), _textures.ChartBlueLine);
                GUI.DrawTexture(new Rect(legendX + 10, legendY + 25, 15, 3), _textures.ChartBlueLine);
                GUI.Label(new Rect(legendX + 28, legendY + 18, 100, 18), "Current Data", legendStyle);

                ChartMath.DrawCircle(new Rect(legendX, legendY + 41, 8, 8), _textures.ChartGreenLine);
                GUI.DrawTexture(new Rect(legendX + 10, legendY + 43, 15, 3), _textures.ChartGreenLine);
                GUI.Label(new Rect(legendX + 28, legendY + 36, 80, 18), "Max Data", legendStyle);
            }
            else
            {
                ChartMath.DrawCircle(new Rect(legendX, legendY + 23, 8, 8), _textures.ChartGreenLine);
                GUI.DrawTexture(new Rect(legendX + 10, legendY + 25, 15, 3), _textures.ChartGreenLine);
                GUI.Label(new Rect(legendX + 28, legendY + 18, 80, 18), "Max Data", legendStyle);
            }
        }

        #endregion

        #region Tooltip

        private void DrawChartTooltip(Rect plotArea, ChartMath.SurvivalCurveData startCurve,
            ChartMath.SurvivalCurveData currentCurve, bool hasCurrentData,
            float cycleReliabilityStart, float cycleReliabilityCurrent, float cycleReliabilityEnd,
            float ratedBurnTime, float testedBurnTime, bool hasTestedBurnTime,
            float maxTime, float overburnPenalty, FloatCurve cycleCurve)
        {
            Vector2 mousePos = Event.current.mousePosition;
            if (!plotArea.Contains(mousePos)) return;

            // Draw hover line
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(new Rect(mousePos.x, plotArea.y, 1, plotArea.height), _textures.ChartHoverLine);
            }

            // Calculate tooltip content
            float mouseT = ChartMath.XPositionToTime(mousePos.x, maxTime, plotArea.x, plotArea.width, _useLogScaleX);
            mouseT = Mathf.Clamp(mouseT, 0f, maxTime);

            string tooltipText = BuildTooltipText(mouseT, ratedBurnTime, testedBurnTime, hasTestedBurnTime,
                cycleReliabilityStart, cycleReliabilityCurrent, cycleReliabilityEnd,
                hasCurrentData, cycleCurve, maxTime, overburnPenalty);

            DrawTooltip(mousePos, tooltipText);
        }

        private string BuildTooltipText(float time, float ratedBurnTime, float testedBurnTime, bool hasTestedBurnTime,
            float cycleReliabilityStart, float cycleReliabilityCurrent, float cycleReliabilityEnd,
            bool hasCurrentData, FloatCurve cycleCurve, float maxTime, float overburnPenalty)
        {
            // Determine zone
            string zoneName;
            string zoneColor;

            if (time <= 5f)
            {
                zoneName = "Engine Startup";
                zoneColor = "#6699CC";
            }
            else if (time <= ratedBurnTime + 5f)
            {
                zoneName = "Rated Operation";
                zoneColor = "#66DD66";
            }
            else if (hasTestedBurnTime && time <= testedBurnTime)
            {
                zoneName = "Tested Overburn";
                zoneColor = "#FFCC44";
            }
            else if (time <= (hasTestedBurnTime ? testedBurnTime : ratedBurnTime) * 2.5f)
            {
                zoneName = "Severe Overburn";
                zoneColor = "#FF6666";
            }
            else
            {
                zoneName = "Maximum Overburn";
                zoneColor = "#CC2222";
            }

            float cycleModifier = cycleCurve.Evaluate(time);
            string valueColor = "#E6D68A";
            string timeStr = ChartMath.FormatTime(time);

            // Calculate survival probabilities at this time
            float baseRateStart = -Mathf.Log(cycleReliabilityStart) / ratedBurnTime;
            float baseRateEnd = -Mathf.Log(cycleReliabilityEnd) / ratedBurnTime;
            float baseRateCurrent = hasCurrentData ? -Mathf.Log(cycleReliabilityCurrent) / ratedBurnTime : 0f;

            float surviveStart = ChartMath.CalculateSurvivalProbAtTime(time, ratedBurnTime, cycleReliabilityStart, baseRateStart, cycleCurve);
            float surviveEnd = ChartMath.CalculateSurvivalProbAtTime(time, ratedBurnTime, cycleReliabilityEnd, baseRateEnd, cycleCurve);
            float surviveCurrent = hasCurrentData ? ChartMath.CalculateSurvivalProbAtTime(time, ratedBurnTime, cycleReliabilityCurrent, baseRateCurrent, cycleCurve) : 0f;

            // Apply cluster math
            if (_clusterSize > 1)
            {
                surviveStart = Mathf.Pow(surviveStart, _clusterSize);
                surviveEnd = Mathf.Pow(surviveEnd, _clusterSize);
                if (hasCurrentData) surviveCurrent = Mathf.Pow(surviveCurrent, _clusterSize);
            }

            string orangeColor = "#FF8033";
            string blueColor = "#7DD9FF";
            string greenColor = "#4DE64D";
            string entityName = _clusterSize > 1 ? "cluster" : "engine";

            string tooltip = $"<b><color={zoneColor}>{zoneName}</color></b>\n\n";
            tooltip += $"This {entityName} has a ";

            if (hasCurrentData)
                tooltip += $"<color={orangeColor}>{surviveStart * 100f:F1}%</color> / <color={blueColor}>{surviveCurrent * 100f:F1}%</color> / <color={greenColor}>{surviveEnd * 100f:F1}%</color>";
            else
                tooltip += $"<color={orangeColor}>{surviveStart * 100f:F1}%</color> / <color={greenColor}>{surviveEnd * 100f:F1}%</color>";

            tooltip += $" chance to survive to <color={valueColor}>{timeStr}</color>\n\n";
            tooltip += $"Cycle modifier: <color={valueColor}>{cycleModifier:F2}×</color>";

            return tooltip;
        }

        private void DrawTooltip(Vector2 mousePos, string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            GUIStyle tooltipStyle = EngineConfigStyles.ChartTooltip;
            tooltipStyle.normal.background = _textures.ChartTooltipBg;

            GUIContent content = new GUIContent(text);
            Vector2 size = tooltipStyle.CalcSize(content);

            float tooltipX = mousePos.x + 15;
            float tooltipY = mousePos.y + 15;

            if (tooltipX + size.x > Screen.width) tooltipX = mousePos.x - size.x - 5;
            if (tooltipY + size.y > Screen.height) tooltipY = mousePos.y - size.y - 5;

            Rect tooltipRect = new Rect(tooltipX, tooltipY, size.x, size.y);
            GUI.Box(tooltipRect, content, tooltipStyle);
        }

        #endregion

        #region Info Panel Integration

        private void DrawInfoPanel(Rect rect, ConfigNode configNode, float ratedBurnTime, float testedBurnTime, bool hasTestedBurnTime,
            float cycleReliabilityStart, float cycleReliabilityEnd, bool hasCurrentData, float cycleReliabilityCurrent,
            float dataPercentage, float currentDataValue, float maxDataValue, float realCurrentData, float realMaxData)
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
                ref _clusterSizeInput, ref _dataValueInput);
        }

        #endregion
    }
}
