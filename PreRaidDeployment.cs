using System;
using System.Collections.Generic;
using EFT;
using EFT.UI;
using EFT.UI.Matchmaker;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UH60FastRopeInsertion
{
    // No screen scanning, IMGUI, server requests or Black Division dependency. Owned by the
    // native location screen, so it shares that screen's canvas/scaling and visibility lifecycle.
    internal sealed class PreRaidDeployment : MonoBehaviour
    {
        private const string PanelName = "UH60_DeploymentSelection";
        private static RaidSettings _lastSettings;
        private RaidSettings _settings;
        private GameObject _panel;
        private UISpawnableToggle _helicopter, _standard;
        private TextMeshProUGUI _description,_heading;
        private MatchMakerSelectionLocationScreen _screen;
        private readonly Vector3[] _corners=new Vector3[4];
        private readonly List<UiBox> _nativeControls=new List<UiBox>();
        private bool _layoutDirty=true,_layoutWarning;
        private bool _refreshing;
        private float _nextRefresh;

        internal static InsertionAvailability GetAvailability(string location, bool scav, bool transition)
        {
            return DeploymentPolicy.Availability(Plugin.Enabled, Plugin.AutomaticRaidStart,
                scav, transition, location, MapSafeSectors.TryGet(location, out _));
        }

        internal static bool ShouldStartRaid(GameWorld world)
        {
            if (world == null || world.MainPlayer == null) return false;
            bool transition = _lastSettings != null && _lastSettings.isInTransition;
            InsertionAvailability availability = GetAvailability(world.LocationId,
                world.MainPlayer.Side == EPlayerSide.Savage, transition);
            bool start = DeploymentPolicy.ShouldInsert(Plugin.SelectedDeployment.Value, availability);
            Plugin.Log.LogInfo("Pre-raid deployment: " + Plugin.SelectedDeployment.Value
                + "; map=" + world.LocationId + "; availability=" + availability + "; automatic=" + start);
            return start;
        }

        internal static void Show(MatchMakerSelectionLocationScreen screen, RaidSettings settings)
        {
            _lastSettings = settings;
            PreRaidDeployment view = screen.GetComponent<PreRaidDeployment>()
                ?? screen.gameObject.AddComponent<PreRaidDeployment>();
            view._settings = settings;
            view._screen = screen;
            if (view._panel == null) view.Create(screen);
            view.Refresh();
        }

        private void Create(MatchMakerSelectionLocationScreen screen)
        {
            UISpawnableToggle template = screen._locationButtonTemplate?._spawnableToggle;
            if (template == null || template.Toggle == null || template._headerLabel == null)
                throw new InvalidOperationException("Native location toggle template is unavailable.");

            _panel = new GameObject(PanelName, typeof(RectTransform));
            _panel.SetActive(false); // Detach clones from the location ToggleGroup BEFORE OnEnable.
            _panel.transform.SetParent(screen.transform, false);
            RectTransform panel = (RectTransform)_panel.transform;
            panel.anchorMin = panel.anchorMax = Vector2.zero;
            panel.pivot = new Vector2(0f,1f);
            _panel.transform.SetAsLastSibling();

            // Background does not intercept clicks on the map outside the actual button bounds.
            Image background = _panel.AddComponent<Image>();
            background.color = new Color(0.035f, 0.038f, 0.033f, 0.92f);
            background.raycastTarget = false;
            ToggleGroup group = _panel.AddComponent<ToggleGroup>();
            group.allowSwitchOff = false;
            _heading = Label("Heading", template._headerLabel, UiText.Get("DEPLOYMENT METHOD"), 20f);
            _helicopter = CloneToggle(template, group, "Helicopter", UiText.Get("HELICOPTER INSERTION"), 16f);
            _standard = CloneToggle(template, group, "Standard", UiText.Get("STANDARD SPAWN"), 368f);
            _helicopter.Toggle.onValueChanged.AddListener(on => Select(on, DeploymentMode.Helicopter));
            _standard.Toggle.onValueChanged.AddListener(on => Select(on, DeploymentMode.Standard));
            _description = Label("Description", template._headerLabel, string.Empty, 16f);
            _description.enableWordWrapping = true;
            _description.overflowMode = TextOverflowModes.Ellipsis;
            Place(_description.rectTransform, 16f, -86f, 688f, 40f);
            _layoutDirty=true; // Only reveal after native details/footer layout has settled.
        }

        private UISpawnableToggle CloneToggle(UISpawnableToggle source, ToggleGroup group, string name,
            string caption, float x)
        {
            UISpawnableToggle clone = Instantiate(source, _panel.transform, false);
            clone.name = "UH60_" + name;
            // Never mutate a source/prefab-external control if a future EFT version moves the fields.
            if (clone.Toggle == null || clone.Toggle == source.Toggle || clone._headerLabel == source._headerLabel
                || !clone.Toggle.transform.IsChildOf(clone.transform))
                throw new InvalidOperationException("Native toggle contains external bindings; refused to change them.");
            clone.Toggle.onValueChanged = new Toggle.ToggleEvent();
            clone.Toggle.onValueChanged.AddListener(clone.Toggle.ToggleSilent);
            clone.Init(group);
            clone.Toggle.SetIsOnWithoutNotify(false);
            clone.InitSpawnableButton(caption, 22, null, null);
            clone._headerLabel.enableAutoSizing = true;
            clone._headerLabel.fontSizeMin = 18f;
            clone._headerLabel.fontSizeMax = 22f;
            clone._headerLabel.enableWordWrapping = false;
            clone._headerLabel.overflowMode = TextOverflowModes.Ellipsis;
            clone.SetMinWidth(332f);
            ContentSizeFitter fitter = clone.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;
            LayoutElement layout = clone.GetComponent<LayoutElement>();
            if (layout != null) { layout.minWidth = 332f; layout.preferredWidth = 332f; }
            Place((RectTransform)clone.transform, x, -38f, 332f, 42f);
            clone.gameObject.SetActive(true);
            return clone;
        }

        private TextMeshProUGUI Label(string name, TextMeshProUGUI style, string text, float size)
        {
            GameObject label = new GameObject(name, typeof(RectTransform));
            label.transform.SetParent(_panel.transform, false);
            TextMeshProUGUI result = label.AddComponent<TextMeshProUGUI>();
            result.font = style.font;
            result.fontSharedMaterial = style.fontSharedMaterial;
            result.fontSize = size;
            result.color = new Color(0.78f, 0.76f, 0.67f, 1f);
            result.alignment = TextAlignmentOptions.TopLeft;
            result.raycastTarget = false;
            result.text = text;
            return result;
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
            rect.localScale = Vector3.one;
        }

        private void Select(bool on, DeploymentMode mode)
        {
            if (!on || _refreshing || _settings == null) return;
            if (mode == DeploymentMode.Helicopter && CurrentAvailability() != InsertionAvailability.Available) return;
            Plugin.SelectedDeployment.Value = mode;
            Refresh();
        }

        private InsertionAvailability CurrentAvailability()
        {
            return GetAvailability(_settings?.SelectedLocation?.Id, _settings?.Side != ESideType.Pmc,
                _settings != null && _settings.isInTransition);
        }

        internal void Refresh()
        {
            if (_panel == null || _helicopter == null || _standard == null || _settings == null) return;
            _refreshing = true;
            try
            {
                InsertionAvailability available = CurrentAvailability();
                bool canFly = available == InsertionAvailability.Available;
                bool selected = DeploymentPolicy.ShouldInsert(Plugin.SelectedDeployment.Value, available);
                _helicopter.Interactable = canFly;
                _standard.Interactable = true;
                if (_helicopter.IsToggled != selected) _helicopter.Toggle.ToggleSilent(selected);
                if (_standard.IsToggled == selected) _standard.Toggle.ToggleSilent(!selected);
                _description.text = DeploymentPolicy.Description(available, Plugin.SelectedDeployment.Value);
                _heading.text = UiText.Get("DEPLOYMENT METHOD");
                _helicopter._headerLabel.text = UiText.Get("HELICOPTER INSERTION");
                _standard._headerLabel.text = UiText.Get("STANDARD SPAWN");
                _layoutDirty=true;
            }
            finally { _refreshing = false; }
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 0.5f;
            Refresh(); // Only this visible screen, to reflect F12 changes. Never scan scene objects.
        }

        private void OnEnable(){_layoutDirty=true;Canvas.willRenderCanvases+=Reflow;}
        private void OnDisable(){Canvas.willRenderCanvases-=Reflow;}
        private void OnRectTransformDimensionsChange(){_layoutDirty=true;}

        private UiBox Box(RectTransform rect)
        {
            rect.GetWorldCorners(_corners);
            float minX=float.PositiveInfinity,minY=minX,maxX=float.NegativeInfinity,maxY=maxX;
            for(int i=0;i<4;i++)
            {
                Vector3 local=_screen.transform.InverseTransformPoint(_corners[i]);
                minX=Mathf.Min(minX,local.x);minY=Mathf.Min(minY,local.y);
                maxX=Mathf.Max(maxX,local.x);maxY=Mathf.Max(maxY,local.y);
            }
            return new UiBox(minX,minY,maxX-minX,maxY-minY);
        }

        private void Reserve(Component component)
        {
            if(component==null || !component.gameObject.activeInHierarchy)return;
            var rect=component.transform as RectTransform;
            if(rect==null)return;
            var box=Box(rect);
            if(box.Width>1f && box.Height>1f)_nativeControls.Add(box);
        }

        private void Reflow()
        {
            if(!_layoutDirty || !isActiveAndEnabled || _screen==null || _panel==null)return;
            _layoutDirty=false;
            var info=_screen._infoPanel;
            if(info==null || !info.gameObject.activeInHierarchy || _settings?.SelectedLocation==null)
            {_panel.SetActive(false);return;}
            var screenRect=_screen.transform as RectTransform;
            if(screenRect==null || info._description==null || info._players==null)return;
            var native=screenRect.rect;
            var viewport=new UiBox(native.xMin,native.yMin,native.width,native.height);
            var column=Box(info._description.rectTransform);
            float bottom=column.Y;
            foreach(var label in new[]{info._players,info._playTime,info._difficulty,info._area})
                if(label!=null && label.gameObject.activeInHierarchy)bottom=Mathf.Min(bottom,Box(label.rectTransform).Y);
            _nativeControls.Clear();
            if(_screen._conditions!=null)
                foreach(var label in _screen._conditions.GetComponentsInChildren<TextMeshProUGUI>(false))
                    if(!string.IsNullOrWhiteSpace(label.text))Reserve(label);
            Reserve(_screen._acceptButton);Reserve(_screen._backButton);
            Reserve(_screen._readyButton);Reserve(_screen._pocketMapButton);
            if(!DeploymentPanelLayout.TryFit(viewport,column,bottom,_nativeControls,out var fit))
            {
                _panel.SetActive(false);
                if(!_layoutWarning){Plugin.Log.LogWarning("Deployment panel: no room below location details without covering native controls; F12 selection remains available.");_layoutWarning=true;}
                return;
            }
            _layoutWarning=false;
            RectTransform panel=(RectTransform)_panel.transform;
            panel.anchoredPosition=new Vector2(fit.X-native.xMin,fit.Top-native.yMin);
            panel.sizeDelta=new Vector2(fit.Width,fit.Height);
            bool compact=fit.Height<DeploymentPanelLayout.Height;
            float width=fit.Width-24f,rowHeight=compact?38f:40f;
            Place(_heading.rectTransform,12f,-8f,width,22f);
            Place((RectTransform)_helicopter.transform,12f,-34f,width,rowHeight);
            Place((RectTransform)_standard.transform,12f,-(compact?78f:82f),width,rowHeight);
            foreach(var button in new[]{_helicopter,_standard})
            {
                button.SetMinWidth(width);
                var layout=button.GetComponent<LayoutElement>();
                if(layout!=null){layout.minWidth=width;layout.preferredWidth=width;}
            }
            _description.gameObject.SetActive(!compact);
            if(!compact)Place(_description.rectTransform,12f,-130f,width,30f);
            _panel.SetActive(true);
        }

        private void OnDestroy() { Canvas.willRenderCanvases-=Reflow;if (_panel != null) Destroy(_panel); }

        internal void DiscardFailedPanel()
        {
            if (_panel != null) { _panel.SetActive(false); Destroy(_panel); }
            _panel = null;
        }
    }

    [HarmonyPatch(typeof(MatchMakerSelectionLocationScreen), nameof(MatchMakerSelectionLocationScreen.Show),
        new[] { typeof(IEftSession), typeof(RaidSettings), typeof(MatchmakerPlayersController) })]
    internal static class PreRaidDeploymentScreenPatch
    {
        private static void Postfix(MatchMakerSelectionLocationScreen __instance, RaidSettings __1)
        {
            try { PreRaidDeployment.Show(__instance, __1); }
            catch (Exception ex)
            {
                __instance.GetComponent<PreRaidDeployment>()?.DiscardFailedPanel();
                Plugin.Log.LogError("Could not create native deployment selector; the original screen is unchanged. " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(MatchMakerSelectionLocationScreen), nameof(MatchMakerSelectionLocationScreen.SelectedLocation), MethodType.Setter)]
    internal static class PreRaidDeploymentLocationPatch
    {
        private static void Postfix(MatchMakerSelectionLocationScreen __instance)
        {
            __instance.GetComponent<PreRaidDeployment>()?.Refresh();
        }
    }
}
