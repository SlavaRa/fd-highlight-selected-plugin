using ASCompletion.Completion;
using ASCompletion.Model;
using PluginCore;
using PluginCore.FRService;
using PluginCore.Helpers;
using PluginCore.Managers;
using PluginCore.Utilities;
using ScintillaNet;
using ScintillaNet.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace HighlightSelection
{
	public class PluginMain : IPlugin
	{
		private string pluginName = "Highlight Selection";
		private string pluginGuid = "1f387fab-421b-42ac-a985-72a03534f731";
		private string pluginHelp = "";
		private string pluginDesc = "A plugin to highlight your selected text";
		private string pluginAuth = "mike.cann@gmail.com";
		private string settingFilename;
		private Settings settings;
        private Timer highlightUnderCursorTimer;
        private Timer tempo;
        private int prevPos;
        private ASResult prevResult;
        private string prevToken;
        private readonly int MARKER_NUMBER = 0;
        private Dictionary<FlagType, Color> flagsToColor;
        private Panel annotationsBar;

		#region Required Properties

        /// <summary>
        /// Api level of the plugin
        /// </summary>
        public int Api
        {
            get { return 1; }
        }

		/// <summary>
		/// Name of the plugin
		/// </summary> 
		public string Name
		{
			get { return pluginName; }
		}

		/// <summary>
		/// GUID of the plugin
		/// </summary>
		public string Guid
		{
			get { return pluginGuid; }
		}

		/// <summary>
		/// Author of the plugin
		/// </summary> 
		public string Author
		{
			get { return pluginAuth; }
		}

		/// <summary>
		/// Description of the plugin
		/// </summary> 
		public string Description
		{
			get { return pluginDesc; }
		}

		/// <summary>
		/// Web address for help
		/// </summary> 
		public string Help
		{
			get { return pluginHelp; }
		}

		/// <summary>
		/// Object that contains the settings
		/// </summary>
		[Browsable(false)]
		public Object Settings
		{
			get { return settings; }
		}

		#endregion

		#region Required Methods

		public void Initialize()
		{
	        InitBasics();
			LoadSettings();
            InitTimers();
            InitFlagsToColor();
			AddEventHandlers();
            UpdateHighlightUnderCursorTimer();
		}

        public void Dispose()
		{
            highlightUnderCursorTimer.Dispose();
            highlightUnderCursorTimer = null;
            tempo.Dispose();
            tempo = null;
		    SaveSettings();
		}

		public void HandleEvent(object sender, NotifyEvent e, HandlingPriority prority)
		{
            ITabbedDocument doc = PluginBase.MainForm.CurrentDocument;
			switch (e.Type)
			{
				case EventType.FileSwitch:
                    RemoveHighlights(doc.SciControl);
                    if (doc.IsEditable)
                    {
                        ScintillaControl sci = doc.SciControl;
                        if (annotationsBar == null || annotationsBar.Parent != doc.SplitContainer.Parent)
                        {
                            annotationsBar = new Panel();
                            annotationsBar.Visible = false;
                            doc.SplitContainer.Parent.Controls.Add(annotationsBar);
                            doc.SplitContainer.Parent.Controls.SetChildIndex(annotationsBar, 0);
                        }
                        sci.MarkerDefine(MARKER_NUMBER, MarkerSymbol.Fullrect);
                        sci.DoubleClick += OnSciDoubleClick;
                        sci.Modified += OnSciModified;
                        tempo.Interval = PluginBase.Settings.DisplayDelay;
                        tempo.Start();
                    }
                    else tempo.Stop();
				    break;
				case EventType.FileSave:
                    RemoveHighlights(doc.SciControl);
                    break;
                case EventType.SettingChanged:
                    InitFlagsToColor();
                    UpdateAnnotationsBar();
                    UpdateHighlightUnderCursorTimer();
				    break;
			}
		}

        #endregion

		#region Custom Methods

        private void InitBasics()
		{
            string path = Path.Combine(PathHelper.DataDir, pluginName);
			if (!Directory.Exists(path)) Directory.CreateDirectory(path);
			settingFilename = Path.Combine(path, "Settings.fdb");
		}

        private void LoadSettings()
		{
			settings = new Settings();
            if (!File.Exists(settingFilename)) SaveSettings();
            else settings = (Settings)ObjectSerializer.Deserialize(settingFilename, settings);
		}

        private void InitTimers()
        {
            highlightUnderCursorTimer = new Timer();
            highlightUnderCursorTimer.Tick += OnHighlighUnderCursorTimerTick;
            tempo = new Timer();
            tempo.Interval = PluginBase.Settings.DisplayDelay;
            tempo.Tick += OnTempoTick;
        }

        private void InitFlagsToColor()
        {
            flagsToColor = new Dictionary<FlagType, Color>();
            flagsToColor[FlagType.Abstract] = settings.AbstractColor;
            flagsToColor[FlagType.TypeDef] = settings.TypeDefColor;
            flagsToColor[FlagType.Enum] = settings.EnumColor;
            flagsToColor[FlagType.Class] = settings.ClassColor;
            flagsToColor[FlagType.ParameterVar] = settings.MemberFunctionColor;
            flagsToColor[FlagType.LocalVar] = settings.LocalVariableColor;
            flagsToColor[FlagType.Constant] = settings.ConstantColor;
            flagsToColor[FlagType.Variable] = settings.VariableColor;
            flagsToColor[FlagType.Setter] = settings.AccessorColor;
            flagsToColor[FlagType.Getter] = settings.AccessorColor;
            flagsToColor[FlagType.Function] = settings.MethodColor;
            flagsToColor[FlagType.Static & FlagType.Constant] = settings.StaticConstantColor;
            flagsToColor[FlagType.Static & FlagType.Variable] = settings.StaticVariableColor;
            flagsToColor[FlagType.Static & FlagType.Setter] = settings.StaticAccessorColor;
            flagsToColor[FlagType.Static & FlagType.Getter] = settings.StaticAccessorColor;
            flagsToColor[FlagType.Static & FlagType.Function] = settings.StaticMethodColor;
        }

        private void AddEventHandlers()
		{
			EventManager.AddEventHandler(this, EventType.FileSwitch | EventType.FileSave | EventType.SettingChanged);
		}

		private void SaveSettings()
		{
			ObjectSerializer.Serialize(settingFilename, settings);
		}

        private bool IsValidFile(string file)
        {
            IProject project = PluginBase.CurrentProject;
            if (project == null) return false;
            string ext = Path.GetExtension(file);
            return (ext == ".as" || ext == ".hx" || ext == ".ls") && PluginBase.CurrentProject.DefaultSearchFilter.Contains(ext);
        }

        private void UpdateHighlightUnderCursorTimer()
        {
            if (settings.HighlightUnderCursorUpdateInteval < HighlightSelection.Settings.DEFAULT_HIGHLIGHT_UNDER_CURSOR_UPDATE_INTERVAL)
                settings.HighlightUnderCursorUpdateInteval = HighlightSelection.Settings.DEFAULT_HIGHLIGHT_UNDER_CURSOR_UPDATE_INTERVAL;
            highlightUnderCursorTimer.Interval = settings.HighlightUnderCursorUpdateInteval;
            if (!settings.HighlightUnderCursorEnabled) highlightUnderCursorTimer.Stop();
        }

        private void AddHighlights(ScintillaControl sci, List<SearchMatch> matches)
        {
            if (matches == null) return;
            int scaleX = 1;
            if (annotationsBar != null)
            {
                annotationsBar.Controls.Clear();
                scaleX = annotationsBar.Height / sci.LineCount;
            }
            Color color = settings.HighlightUnderCursorEnabled && prevResult != null ? GetHighlightColor() : settings.HighlightColor;
            int style = (int)settings.HighlightStyle;
            int mask = 1 << sci.StyleBits;
            int es = sci.EndStyled;
            bool addLineMarker = settings.AddLineMarker;
            int argb = DataConverter.ColorToInt32(color);
            foreach (SearchMatch match in matches)
            {
                int start = sci.MBSafePosition(match.Index);
                int end = start + sci.MBSafeTextLength(match.Value);
                sci.SetIndicStyle(0, style);
                sci.SetIndicFore(0, argb);
                sci.StartStyling(start, mask);
                sci.SetStyling(end - start, mask);
                sci.StartStyling(es, mask - 1);
                int line = sci.LineFromPosition(start);
                if (addLineMarker)
                {
                    sci.MarkerAdd(line, MARKER_NUMBER);
                    sci.MarkerSetBack(MARKER_NUMBER, argb);
                }
                if (annotationsBar != null)
                {
                    Button item = new Button();
                    item.FlatStyle = FlatStyle.Flat;
                    item.Size = new Size(annotationsBar.Width, Math.Max(10, scaleX));
                    item.BackColor = color;
                    item.Location = new Point(0, annotationsBar.Height * line / sci.LineCount);
                    item.Tag = line;
                    item.MouseClick += (s, e) => sci.GotoLine((int)((Button)s).Tag);
                    annotationsBar.Controls.Add(item);
                }
            }
            prevPos = sci.CurrentPos;
        }

        private Color GetHighlightColor()
        {
            if (settings.HighlightUnderCursorEnabled && prevResult != null && !prevResult.IsNull())
            {
                if (prevResult.IsPackage) return settings.PackageColor;
                FlagType flags = prevResult.Type != null && prevResult.Member == null ? prevResult.Type.Flags : prevResult.Member.Flags;
                if ((flags & FlagType.Abstract) > 0) return flagsToColor[FlagType.Abstract];
                if ((flags & FlagType.TypeDef) > 0) return flagsToColor[FlagType.TypeDef];
                if ((flags & FlagType.Enum) > 0) return flagsToColor[FlagType.Enum];
                if ((flags & FlagType.Class) > 0) return flagsToColor[FlagType.Class];
                if ((flags & FlagType.ParameterVar) > 0) return flagsToColor[FlagType.ParameterVar];
                if ((flags & FlagType.LocalVar) > 0) return flagsToColor[FlagType.LocalVar];
                if ((flags & FlagType.Static) == 0)
                {
                    if ((flags & FlagType.Constant) > 0) return flagsToColor[FlagType.Constant];
                    if ((flags & FlagType.Variable) > 0) return flagsToColor[FlagType.Variable];
                    if ((flags & FlagType.Setter) > 0) return flagsToColor[FlagType.Setter];
                    if ((flags & FlagType.Getter) > 0) return flagsToColor[FlagType.Getter];
                    if ((flags & FlagType.Function) > 0) return flagsToColor[FlagType.Function];
                }
                else
                {
                    if ((flags & FlagType.Constant) > 0) return flagsToColor[FlagType.Static & FlagType.Constant];
                    if ((flags & FlagType.Variable) > 0) return flagsToColor[FlagType.Static & FlagType.Variable];
                    if ((flags & FlagType.Setter) > 0) return flagsToColor[FlagType.Static & FlagType.Setter];
                    if ((flags & FlagType.Getter) > 0) return flagsToColor[FlagType.Static & FlagType.Getter];
                    if ((flags & FlagType.Function) > 0) return flagsToColor[FlagType.Static & FlagType.Function];
                }
            }
            return settings.HighlightColor;
        }

        private void RemoveHighlights(ScintillaControl sci)
        {
            if (sci != null)
            {
                int es = sci.EndStyled;
                int mask = 1 << sci.StyleBits;
                sci.StartStyling(0, mask);
                sci.SetStyling(sci.TextLength, 0);
                sci.StartStyling(es, mask - 1);
                if (settings.AddLineMarker) sci.MarkerDeleteAll(MARKER_NUMBER);
            }
            prevPos = -1;
            prevToken = string.Empty;
            prevResult = null;
            if (annotationsBar != null) annotationsBar.Controls.Clear();
        }

        private List<SearchMatch> GetResults(ScintillaControl sci, string text)
        {
            if (string.IsNullOrEmpty(text) || Regex.IsMatch(text, "[^a-zA-Z0-9_$]")) return null;
            FRSearch search = new FRSearch(text);
            search.WholeWord = settings.WholeWords;
            search.NoCase = !settings.MatchCase;
            search.Filter = SearchFilter.None;
            return search.Matches(sci.Text);
        }

        private void UpdateHighlightUnderCursor(ScintillaControl sci)
        {
            string file = PluginBase.MainForm.CurrentDocument.FileName;
            if (!IsValidFile(file)) return;
            int currentPos = sci.CurrentPos;
            string newToken = sci.GetWordFromPosition(currentPos);
            if (!string.IsNullOrEmpty(newToken)) newToken = newToken.Trim();
            if (string.IsNullOrEmpty(newToken)) RemoveHighlights(sci);
            else 
            {
                if (prevResult == null && prevToken == newToken) return;
                ASResult result = IsValidFile(file) ? ASComplete.GetExpressionType(sci, sci.WordEndPosition(currentPos, true)) : null;
                if (result == null || result.IsNull()) RemoveHighlights(sci);
                else 
                {
                    if (prevResult != null && (result.Member != prevResult.Member || result.Type != prevResult.Type || result.Path != prevResult.Path)) return;
                    RemoveHighlights(sci);
                    prevToken = newToken;
                    prevResult = result;
                    List<SearchMatch> matches = FilterResults(GetResults(sci, prevToken), result, sci);
                    if (matches == null || matches.Count == 0) return;
                    highlightUnderCursorTimer.Stop();
                    AddHighlights(sci, matches);
                }
            }
        }

        private List<SearchMatch> FilterResults(List<SearchMatch> matches, ASResult exprType, ScintillaControl sci)
        {
            if (matches == null || matches.Count == 0) return null;
            MemberModel contextMember = null;
            int lineFrom = 0;
            int lineTo = sci.LineCount;
            FlagType localVarMask = FlagType.LocalVar | FlagType.ParameterVar;
            bool isLocalVar = false;
            if (exprType.Member != null)
            {
                if ((exprType.Member.Flags & localVarMask) > 0)
                {
                    contextMember = exprType.Context.ContextFunction;
                    lineFrom = contextMember.LineFrom;
                    lineTo = contextMember.LineTo;
                    isLocalVar = true;
                }
            }
            List<SearchMatch> newMatches = new List<SearchMatch>();
            foreach (SearchMatch m in matches)
            {
                if (m.Line < lineFrom || m.Line > lineTo) continue;
                int pos = sci.MBSafePosition(m.Index);
                exprType = ASComplete.GetExpressionType(sci, sci.WordEndPosition(pos, true));
                if (exprType != null && (exprType.InClass == null || exprType.InClass == prevResult.InClass))
                {
                    MemberModel member = exprType.Member;
                    if (!isLocalVar)
                    {
                        if ((exprType.Type != null && member == null) || (member != null && (member.Flags & localVarMask) == 0)) newMatches.Add(m);
                    }
                    else if (member != null && (member.Flags & localVarMask) > 0) newMatches.Add(m);
                }
            }
            return newMatches;
        }

        private void UpdateAnnotationsBar()
        {
            ScintillaControl sci = PluginBase.MainForm.CurrentDocument.SciControl;
            annotationsBar.Size = new System.Drawing.Size(6, sci.Height - 17);
            annotationsBar.Location = new System.Drawing.Point(sci.Width - 21, annotationsBar.Location.Y);
            annotationsBar.BackColor = System.Drawing.Color.Transparent;
            annotationsBar.Visible = settings.EnableAnnotationBar
                                && annotationsBar.Height < sci.LineCount * sci.TextHeight(sci.LineFromPosition(sci.CurrentPos));
        }

        private void OnSciDoubleClick(ScintillaControl sender)
        {
            RemoveHighlights(sender);
            prevResult = null;
            prevToken = sender.SelText.Trim();
            AddHighlights(sender, GetResults(sender, prevToken));
        }

        private void OnSciModified(ScintillaControl sender, int position, int modificationType, string text, int length, int linesAdded, int line, int intfoldLevelNow, int foldLevelPrev)
        {
            highlightUnderCursorTimer.Stop();
            tempo.Stop();
            RemoveHighlights(sender);
            UpdateAnnotationsBar();
            tempo.Interval = PluginBase.Settings.DisplayDelay;
            tempo.Start();
        }

        private void OnTempoTick(object sender, EventArgs e)
        {
            ITabbedDocument document = PluginBase.MainForm.CurrentDocument;
            if (document == null) return;
            ScintillaControl sci = document.SciControl;
            if (sci == null) return;
            int currentPos = sci.CurrentPos;
            if (currentPos == prevPos) return;
            string newToken = sci.GetWordFromPosition(currentPos);
            if (settings.HighlightUnderCursorEnabled)
            {
                if (prevPos != currentPos) highlightUnderCursorTimer.Stop();
                if (prevResult != null)
                {
                    ASResult result = IsValidFile(document.FileName) ? ASComplete.GetExpressionType(sci, sci.WordEndPosition(currentPos, true)) : null;
                    if (result == null || result.IsNull() || result.Member != prevResult.Member || result.Type != prevResult.Type || result.Path != prevResult.Path)
                    {
                        RemoveHighlights(sci);
                        highlightUnderCursorTimer.Start();
                    }
                }
                else
                {
                    RemoveHighlights(sci);
                    highlightUnderCursorTimer.Start();
                }

            }
            else if (newToken != prevToken) RemoveHighlights(sci);
            prevPos = currentPos;
        }

        private void OnHighlighUnderCursorTimerTick(object sender, EventArgs e)
        {
            ScintillaControl sci = PluginBase.MainForm.CurrentDocument.SciControl;
            if (sci != null) UpdateHighlightUnderCursor(sci);
        }

        #endregion
    }
}