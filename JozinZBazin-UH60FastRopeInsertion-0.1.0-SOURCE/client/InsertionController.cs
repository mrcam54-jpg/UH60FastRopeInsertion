using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.CameraControl;
using EFT.EnvironmentEffect;
using UnityEngine;
using UnityEngine.AI;

namespace UH60FastRopeInsertion
{
    internal sealed partial class InsertionController : MonoBehaviour
    {
        private enum SceneState
        {
            Idle,
            WaitingForSquad,
            ValidatingSector,
            Preparing,
            Approach,
            StableHover,
            OpeningDoors,
            DeployingRopes,
            Descending,
            RetractingRopes,
            Departing,
            Complete,
            Failed,
        }

        private sealed class ActorSlot : IDisposable
        {
            internal LiveActorState Live;
            internal CinematicActor Double;
            internal int Side;
            internal int LaneSlot;
            internal float StartTime;
            internal bool SlideSoundStarted;
            internal bool Released;
            internal bool HasDescentStart;
            internal Vector3 DescentStartRoot;

            public void Dispose()
            {
                Double?.Dispose();
                Double = null;
                Live?.Dispose();
                Live = null;
            }
        }

        internal static InsertionController Instance { get; private set; }

        private readonly List<ActorSlot> _actors = new List<ActorSlot>();
        private Coroutine _sequence;
        private GameWorld _world;
        private MapSafeSector _sector;
        private LandingSite _landingSite;
        private Uh60Visual _aircraft;
        private SteelRope _leftRope;
        private SteelRope _rightRope;
        private CabinRopeBundle _cabinRopeBundle;
        private CabinRopeBundle _rightCabinRopeBundle;
        private bool _useRightRope;
        private InsertionEffects _effects;
        private Camera _camera;
        private TOD_Camera _skyCamera;
        private CinematicEnvironment _environment;
        private bool _cameraActive;
        private bool _inputLocked;
        private bool _hasCameraPose;
        private Vector3 _cameraPosition;
        private Quaternion _cameraRotation;
        private float _lookYaw;
        private float _lookPitch;
        private Vector3 _landing;
        private Vector3 _hover;
        private Vector3 _offstage;
        private Vector3 _aircraftPosition;
        private Quaternion _aircraftRotation = Quaternion.identity;
        private float _stateTime;
        private float _sceneTime;
        private float _flightSceneStart;
        private SceneState _state = SceneState.Idle;
        private string _status = "idle";
        private string _validation = "not run";
        private string _failureDetail = string.Empty;
        private bool _cleaningUp;
        private bool _autoStartedThisRaid;
        private IcebreakerAirframe _airframe;
        private CinematicVisibility _visibility;
        private float _throwProgress, _retraction;
        private float _descentClock, _descentDuration;
        private Vector3 _leftGround, _rightGround;
        private const float DescentBodyOffset=SupportedRopePose.BodyOffset;
        private bool _inspection;
        private Vector3 _inspectionLocalPosition;
        private float _inspectionYaw, _inspectionPitch;
        private float _originalNearClip;
        private CinematicFieldOfView _fieldOfView;
        private int _originalCameraMask;
        private bool _presentationMaskOwned;
        private Vector3 _hoverLeftOffset, _hoverRightOffset;
        private Quaternion _previewLeftFacing, _previewRightFacing;
        private bool _preparationCover;
        private PresentationMetrics _metrics;
        private float _coverFadeStart = -1f;
        private Vector3 _previousCabinVelocity, _previousCabinPosition, _cabinResponse;
        private Vector3 _filteredCabinAcceleration;
        private Quaternion _previousCabinRotation;
        private bool _cabinMotionReady;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Camera.onPreCull += OnCameraPreCull;
        }

        private void OnDestroy()
        {
            Camera.onPreCull -= OnCameraPreCull;
            Cleanup(false);
            if (Instance == this) Instance = null;
        }

        internal void OnRaidStarted(GameWorld world)
        {
            if (_sequence != null)
            {
                StopCoroutine(_sequence);
                _sequence = null;
                Cleanup(false);
            }
            _world = world;
            _autoStartedThisRaid = false;
            if (!PreRaidDeployment.ShouldStartRaid(world)) return;
            TryStart(false);
        }

        private void Update()
        {
            AuthoringInput();
            if (_sequence != null && !IsRaidValid())
            {
                StopCoroutine(_sequence); _sequence = null; Cleanup(false); return;
            }
            if (_sequence != null) _sceneTime += Time.unscaledDeltaTime;
            if (Plugin.QuickTestKey.Value.IsDown() && Plugin.Enabled.Value)
            {
                if (_sequence != null)
                {
                    StopCoroutine(_sequence);
                    _sequence = null;
                    Cleanup(false);
                }
                _world = Comfort.Common.Singleton<GameWorld>.Instantiated
                    ? Comfort.Common.Singleton<GameWorld>.Instance
                    : null;
                TryStart(true);
            }

            if (_sequence != null && Plugin.InspectionKey.Value.IsDown() && _actors.Count > 0 && _aircraft != null && _camera != null)
                ToggleInspection();
            if (_cameraActive && _inspection)
            {
                _inspectionYaw += Input.GetAxisRaw("Mouse X") * Plugin.CameraSensitivity.Value;
                _inspectionPitch = Mathf.Clamp(_inspectionPitch - Input.GetAxisRaw("Mouse Y") * Plugin.CameraSensitivity.Value, -85f, 85f);
                Quaternion look = Quaternion.Euler(_inspectionPitch, _inspectionYaw, 0f);
                Vector3 move = new Vector3((Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0),
                    (Input.GetKey(KeyCode.E) ? 1 : 0) - (Input.GetKey(KeyCode.Q) ? 1 : 0),
                    (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0));
                _inspectionLocalPosition += look * Vector3.ClampMagnitude(move, 1f) * Time.unscaledDeltaTime
                    * Plugin.InspectionSpeed.Value * (Input.GetKey(KeyCode.LeftShift) ? 3f : 1f);
            }
            else if (_cameraActive)
            {
                _lookYaw = Mathf.Clamp(_lookYaw + Input.GetAxisRaw("Mouse X") * Plugin.CameraSensitivity.Value,
                    -Plugin.CameraYawLimit.Value, Plugin.CameraYawLimit.Value);
                _lookPitch = Mathf.Clamp(_lookPitch - Input.GetAxisRaw("Mouse Y") * Plugin.CameraSensitivity.Value,
                    -Plugin.CameraPitchUpLimit.Value, Plugin.CameraPitchDownLimit.Value);
            }

        }

        private void LateUpdate()
        {
            foreach(var actor in _actors) if(actor.Released) actor.Live?.ObserveHandoff(Time.deltaTime);
            long started=System.Diagnostics.Stopwatch.GetTimestamp();
            try { UpdatePresentation(); }
            catch (Exception ex)
            {
                Plugin.Log.LogError("Late presentation failed; restoring actors/input: " + ex);
                if (_sequence != null) StopCoroutine(_sequence);
                _sequence = null;
                Fail("presentation update failed; see log");
            }
            if(_sequence!=null)
            {
                _metrics?.Scene(System.Diagnostics.Stopwatch.GetTimestamp()-started);
                _metrics?.Flush(_state.ToString());
            }
        }

        private void UpdatePresentation()
        {
            if (_aircraft != null)
            {
                _aircraft.TickRotors(Time.unscaledDeltaTime, 1f);
                UpdateCabinResponse();
                // Animator has finished moving the airframe. Pose occupants and ropes in that same
                // frame before deriving the eye camera; never smooth a cached world-space camera.
                if (_state >= SceneState.Approach && _state <= SceneState.DeployingRopes)
                {
                    long stage=System.Diagnostics.Stopwatch.GetTimestamp();
                    PoseCabinActors(_sceneTime, _throwProgress);
                    _metrics?.Stage(PresentationStage.Cabin,System.Diagnostics.Stopwatch.GetTimestamp()-stage);
                }
                if (_state >= SceneState.Approach && _state <= SceneState.RetractingRopes)
                {
                    long stage=System.Diagnostics.Stopwatch.GetTimestamp();
                    SimulateRopes();
                    _metrics?.Stage(PresentationStage.Ropes,System.Diagnostics.Stopwatch.GetTimestamp()-stage);
                }
                if (_state >= SceneState.Approach && _state <= SceneState.DeployingRopes)
                {
                    long stage=System.Diagnostics.Stopwatch.GetTimestamp();
                    _cabinRopeBundle?.Tick(_aircraft, _throwProgress, _leftRope,_leftGround,
                        _actors.Count > 0 ? _actors[0].Double : null,_userChoreography);
                    if(_useRightRope) _rightCabinRopeBundle?.Tick(_aircraft,
                        _userChoreography?_throwProgress:Mathf.Clamp01((_throwProgress-.08f)/.92f),_rightRope,_rightGround,null,_userChoreography);
                    _metrics?.Stage(PresentationStage.Bundle,System.Diagnostics.Stopwatch.GetTimestamp()-stage);
                }
                if (_state == SceneState.Descending)
                {
                    long stage=System.Diagnostics.Stopwatch.GetTimestamp();
                    UpdateActorsDuringDescent(_descentClock, _descentDuration);
                    _metrics?.Stage(PresentationStage.Descent,System.Diagnostics.Stopwatch.GetTimestamp()-stage);
                }
            }
            ApplyCameraPoseForWorldSystems();
        }

        private void OnCameraPreCull(Camera camera)
        {
            if(camera==_camera && _presentationMaskOwned)camera.cullingMask|=1<<CabinLighting.Layer;
            if (camera == _camera) ApplyCameraPoseForWorldSystems();
        }

        internal void ApplyCameraPoseForWorldSystems()
        {
            if (!_cameraActive || _actors.Count == 0) return;
            long started=System.Diagnostics.Stopwatch.GetTimestamp();
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;
            ResolveCameraPose();
            if (!_hasCameraPose) return;
            _camera.transform.SetPositionAndRotation(_cameraPosition, _cameraRotation);
            _fieldOfView?.Apply(Plugin.CabinFovBonus.Value);
            _camera.cullingMask|=1<<CabinLighting.Layer;
            _camera.ResetWorldToCameraMatrix();
            _camera.ResetCullingMatrix();
            // TOD_Camera's component callback may have run BEFORE Camera.onPreCull moved the
            // cinematic viewpoint. Synchronize the existing raid dome after the final eye pose.
            // Do not disable scattering, exposure, grading or other normal EFT post effects.
            if (_skyCamera != null && _skyCamera.isActiveAndEnabled) _skyCamera.OnPreCull();
            _visibility?.Observe(_cameraPosition);
            _environment?.Observe();
            _metrics?.World(System.Diagnostics.Stopwatch.GetTimestamp()-started);
        }

        internal bool TryOverrideEnvironment(EFT.IPlayer player, out IndoorTrigger trigger)
        {
            trigger=null;
            return _cameraActive && _environment!=null && _environment.OverrideTrigger(player,out trigger);
        }

        private void TryStart(bool manual)
        {
            if (_sequence != null || _world == null || _world.MainPlayer == null) return;
            _metrics=new PresentationMetrics();
            string locationId = (_world.LocationId ?? string.Empty).Trim().ToLowerInvariant();
            if (MapSafeSectors.IsFactory(locationId))
            {
                SetState(SceneState.Failed, "Factory is intentionally excluded");
                return;
            }
            if (!MapSafeSectors.TryGet(locationId, out _sector))
            {
                SetState(SceneState.Failed, "No authored safe sector for " + locationId);
                return;
            }
            _landingSite=LandingRequest.Resolve(locationId);
            if(_landingSite!=null)_sector=_landingSite.Sector;
            if (!manual && _autoStartedThisRaid) return;
            InsertionApi.BeginAttempt(_landingSite?.Id??locationId+".default",manual);
            _autoStartedThisRaid = true;
            _sceneTime = 0f;
            _failureDetail = string.Empty;
            _preparationCover = !manual;
            _coverFadeStart = -1f;
            GamePlayerOwner.SetIgnoreInputInNPCDialog(true);
            _inputLocked = true;
            _sequence = StartCoroutine(SafeRun(RunSequence(manual)));
        }

        private IEnumerator SafeRun(IEnumerator inner)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(inner);
            while (stack.Count > 0)
            {
                object current;
                try
                {
                    var active = stack.Peek();
                    if (!active.MoveNext()) { (active as IDisposable)?.Dispose(); stack.Pop(); continue; }
                    current = active.Current;
                    var nested = current as IEnumerator;
                    if (nested != null) { stack.Push(nested); continue; }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError("Unhandled insertion-scene exception; restoring every live actor: " + ex);
                    _failureDetail = ex.GetType().Name + ": " + ex.Message.Replace('\n', ' ').Replace('\r', ' ');
                    if (_failureDetail.Length > 320) _failureDetail = _failureDetail.Substring(0, 320);
                    SetState(SceneState.Failed, "runtime exception; actors restored");
                    Cleanup(false);
                    _sequence = null;
                    yield break;
                }
                yield return current;
            }
        }

        private IEnumerator RunSequence(bool manual)
        {
            SetState(SceneState.WaitingForSquad, manual ? "manual test: waiting for squad" : "waiting for PIT squad");
            float wait = manual ? 0.15f : Plugin.SquadWaitSeconds.Value;
            while (wait > 0f && IsRaidValid())
            {
                wait -= Time.unscaledDeltaTime;
                yield return null;
            }
            if (!IsRaidValid())
            {
                Fail("raid ended before preparation");
                yield break;
            }

            SetState(SceneState.Preparing, "building aircraft, ropes and visual doubles");
            if (Plugin.AircraftModel.Value == "Icebreaker")
            {
                _airframe = new IcebreakerAirframe();
                yield return _airframe.Prepare();
                if (!_airframe.Ready || !string.IsNullOrEmpty(_airframe.Error))
                { Fail(_airframe.Error ?? "Icebreaker airframe preparation failed"); yield break; }
            }
            if (!Uh60Visual.TryCreate(out _aircraft, out string aircraftError))
            {
                Fail(aircraftError);
                yield break;
            }
            _aircraft.AttachAirframe(_airframe);
            List<SquadMember> members = SquadResolver.Resolve(_world.MainPlayer, 5);
            _useRightRope=members.Count>=3;
            yield return PlanLanding(manual);
            if(!_landingPlanned)
            { Fail("no safe marked LZ/approach accepted before flight: "+_validation);yield break; }
            Quaternion routeRotation=_plannedRouteRotation;
            Vector3 inbound=routeRotation*Vector3.forward;
            Plugin.Log.LogInfo("Safe sector accepted: " + _sector.LocationId + " " + _landing + " — " + _validation
                + "; hover socket offsets L=" + _hoverLeftOffset + " R=" + _hoverRightOffset);
            _aircraft.PackedRopeLength = _hoverLeftOffset.y - (_leftGround.y - _landing.y) + 2.8f;
            _leftRope = new SteelRope("UH60_LeftSteelRope", -1, _airframe?.RopeMaterial, _airframe?.OriginalRopeMesh);
            _rightRope = new SteelRope("UH60_RightSteelRope", 1, _airframe?.RopeMaterial, _airframe?.OriginalRopeMesh);
            _cabinRopeBundle = new CabinRopeBundle(_airframe?.RopeMaterial, _airframe?.OriginalRopeMesh);
            _rightCabinRopeBundle = new CabinRopeBundle(_airframe?.RopeMaterial, _airframe?.OriginalRopeMesh,1);
            _effects = new InsertionEffects();
            // Tactical Services' authored approach settles b_vhc_main exactly 13 m
            // above the request root. The sector height remains a clearance test only.
            _hover = _landing + Vector3.up * 13f;
            // Keep the hidden live bodies on a valid surface rather than under the terrain,
            // where player-dependent culling and physics were selecting the underground world.
            _offstage = _landing;

            if (!BuildActors(members))
            {
                Fail("could not create the visual player double");
                yield break;
            }
            AssignLandings();
            Plugin.Log.LogInfo("Insertion roster: " + string.Join(", ", _actors.Select((actor, index) =>
                index + ":" + (actor.Live.Player.Profile?.Nickname ?? actor.Live.Player.ProfileId)
                + " lane=" + (actor.Side < 0 ? "L" : "R") + actor.LaneSlot)));

            _aircraft.BeginTacticalFlight(_landing, routeRotation, Plugin.FlightSpeedMultiplier.Value);
            _flightSceneStart=_sceneTime;
            _aircraftPosition = _aircraft.RootTransform.position;
            _aircraftRotation = _aircraft.RootTransform.rotation;
            // As in the reference cut-scene, the two side doors are already open
            // during the inbound flight and the left rope is coiled at the sill.
            _aircraft.SetDoorProgress(1f);
            PoseCabinActors(0f, 0f);
            ScheduleUserActors();
            _cabinRopeBundle.Tick(_aircraft, 0f,_leftRope,_leftGround);
            CacheCameraPose();
            _camera = CameraManager.Instance != null ? CameraManager.Instance.Camera : Camera.main;
            if (_camera == null && _world.MainPlayer.CameraContainer != null)
            {
                _camera = _world.MainPlayer.CameraContainer.GetComponentInChildren<Camera>(true);
            }
            if (_camera == null)
            {
                Fail("active EFT camera was not found");
                yield break;
            }
            BeginCameraAndInputLock();
            CacheCameraPose();
            foreach (ActorSlot actor in _actors)
            {
                actor.Live.FreezeAndHide();
                actor.Live.HoldOffstage(_offstage);
                actor.Double.SetVisible(true);
            }
            _visibility = new CinematicVisibility(_camera);
            _environment = new CinematicEnvironment(_world.MainPlayer,_camera);
            _cabinMotionReady = false;
            _preparationCover = false;
            _coverFadeStart = Time.unscaledTime + 1.7f;
            yield return null;

            SetState(SceneState.Approach, "Tactical Services authored UH-60 approach");
            float flightTimeout = 70f / Mathf.Max(0.6f, Plugin.FlightSpeedMultiplier.Value);
            float flightTime = 0f;
            while (!_aircraft.HasReachedTacticalHover && flightTime < flightTimeout && IsRaidValid())
            {
                flightTime += Time.unscaledDeltaTime;
                _aircraftPosition = _aircraft.RootTransform.position;
                _aircraftRotation = _aircraft.RootTransform.rotation;
                _throwProgress = 0f;
                HoldLiveActors();
                CacheCameraPose();
                UpdateEffects(0f);
                yield return null;
            }
            if (!_aircraft.HasReachedTacticalHover)
            {
                Fail("Tactical Services approach did not reach its authored hover before timeout");
                yield break;
            }

            _hover = _aircraft.RootTransform.position;
            SetState(SceneState.StableHover, "authored hover reached; holding before rope work");
            float stable = 0f;
            while (stable < 0.9f && IsRaidValid())
            {
                float dt = Time.unscaledDeltaTime;
                _aircraftPosition = _aircraft.RootTransform.position;
                _aircraftRotation = _aircraft.RootTransform.rotation;
                _throwProgress = 0f;
                HoldLiveActors();
                CacheCameraPose();
                stable += dt;
                yield return null;
            }

            if (!ValidateDoorLanes(_aircraft.GetDoorAnchor(-1), _aircraft.GetDoorAnchor(1),
                _aircraft.GetRopeFacing(-1), _aircraft.GetRopeFacing(1), out string laneReason))
            { Fail("final hover lane validation: " + laneReason); yield break; }
            foreach (var actor in _actors)
            {
                Vector3 forward = _aircraft.GetRopeFacing(actor.Side) * Vector3.forward;
                actor.Live.LandingPosition = (actor.Side < 0 ? _leftGround : _rightGround) - forward * DescentBodyOffset;
                actor.Live.LandingRotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(forward, Vector3.up).normalized, Vector3.up);
                actor.Double.LandingPosition = actor.Live.LandingPosition;
                actor.Double.LandingRotation = actor.Live.LandingRotation;
            }
            Plugin.Log.LogInfo("Raycast lane surfaces locked: L=" + _leftGround + " R=" + _rightGround);

            SetState(SceneState.OpeningDoors, "stable hover confirmed; door team bracing");
            yield return RunTimed(0.85f, p =>
            {
                _aircraft.SetDoorProgress(1f);
                _throwProgress = 0f;
                UpdateHoverPose(inbound);
                CacheCameraPose();
            });

            SetState(SceneState.DeployingRopes, _useRightRope?"left operator throws; right door team kicks its cable":"left door team deploying one shared cable");
            _effects.PlayDeploy();
            yield return RunTimed(8.0f, p =>
            {
                UpdateHoverPose(inbound);
                float throwProgress = p;
                float leftDeployment = Mathf.Pow(Mathf.Clamp01(Mathf.InverseLerp(0.74f, 1f, p)), 2f);
                float rightDeployment = Mathf.Pow(Mathf.Clamp01(Mathf.InverseLerp(0.72f, 1f, p)), 2f);
                // Authored payout owns the visible centreline. Keep its hidden destination fully
                // settled before blending into it, not the old late-release partial deployment.
                if(_userChoreography)leftDeployment=rightDeployment=1f;
                TickRopes(leftDeployment, rightDeployment, 0f);
                _throwProgress = throwProgress;
                CacheCameraPose();
                UpdateEffects(p);
            });

            SetState(SceneState.Descending, _useRightRope?"two-lane staggered descent":"single-lane staggered descent");
            float descentDuration = Plugin.DescentDuration.Value;
            _descentDuration = descentDuration;
            float lastEnd = _actors.Max(actor => actor.StartTime + descentDuration + InsertionTiming.Touchdown+.08f);
            float descentClock = 0f;
            while (descentClock < lastEnd && IsRaidValid())
            {
                descentClock += Time.unscaledDeltaTime;
                _descentClock = descentClock;
                UpdateHoverPose(inbound);
                TickRopes(1f, 1f, 0f);
                HoldLiveActors();
                CacheCameraPose();
                UpdateEffects(1f);
                yield return null;
            }

            foreach (ActorSlot actor in _actors)
            {
                if (!actor.Released) ReleaseActor(actor);
            }

            float dwell = 1.6f;
            while (dwell > 0f && IsRaidValid())
            {
                dwell -= Time.unscaledDeltaTime;
                UpdateHoverPose(inbound);
                TickRopes(1f, 1f, 0f);
                UpdateEffects(0.78f);
                yield return null;
            }

            SetState(SceneState.RetractingRopes, "slow rope retraction while hovering");
            _effects.PlayRetract();
            yield return RunTimed(5.8f, p =>
            {
                UpdateHoverPose(inbound);
                TickRopes(1f, 1f, SmootherStep(p));
                UpdateEffects(1f - p);
            });

            SetState(SceneState.Departing, "Tactical Services authored departure");
            _aircraft.BeginTacticalDeparture();
            float departureTime = 0f;
            float departureTimeout = 58f / Mathf.Max(0.6f, Plugin.FlightSpeedMultiplier.Value);
            while (!_aircraft.HasClearedLanding(420f) && departureTime < departureTimeout && IsRaidValid())
            {
                departureTime += Time.unscaledDeltaTime;
                _aircraftPosition = _aircraft.RootTransform.position;
                _aircraftRotation = _aircraft.RootTransform.rotation;
                float distance = Vector3.Distance(_aircraftPosition, _landing);
                _aircraft.SetAudioVolume(1f - Mathf.Clamp01((distance - 140f) / 420f));
                yield return null;
            }
            if (!_aircraft.HasClearedLanding(420f))
            {
                Plugin.Log.LogWarning("Authored departure cleanup timeout reached at "
                    + Vector3.Distance(_aircraft.RootTransform.position, _landing).ToString("F1") + " m.");
            }

            SetState(SceneState.Complete, "scene complete");
            Cleanup(true);
            _sequence = null;
        }

        private bool BuildActors(List<SquadMember> members)
        {
            float descent = Plugin.DescentDuration.Value;
            _userChoreography=Plugin.CabinClipEnabled.Value&&Plugin.UserCabinActions.Value;
            float[] starts=InsertionTiming.Starts(descent,_userChoreography);
            for (int i = 0; i < members.Count && i < 5; i++)
            {
                if (!CinematicActor.TryCreate(members[i].Player, i, out CinematicActor visual, out string error))
                {
                    Plugin.Log.LogError("Actor " + i + " clone failed: " + error);
                    if (i == 0) return false;
                    continue;
                }
                // The thrower and the braced guard share the left door.  The
                // remaining passengers then feed both persistent ropes.
                int side=InsertionTiming.Side(i);
                int laneSlot = i < 2 ? 0 : (i - 2) / 2;
                ActorSlot actor = new ActorSlot
                {
                    Live = new LiveActorState(members[i]),
                    Double = visual,
                    Side = side,
                    LaneSlot = laneSlot,
                    StartTime = starts[i],
                };
                visual.LaneSide = side;
                visual.LaneSlot = laneSlot;
                visual.DescentDelay = actor.StartTime;
                // Register ownership before a target-pose preflight can throw, so cleanup
                // also disposes the failing visual rather than leaving an orphaned clone.
                _actors.Add(actor);
                if(_airframe!=null)visual.SetNativeSource(i==0?_airframe.SeatedMotion:_airframe.GuardMotion);
                if (i == 0) visual.SetMainActorFirstPersonPresentation(true);
            }
            return _actors.Count > 0;
        }

        private void AssignLandings()
        {
            for (int i = 0; i < _actors.Count; i++)
            {
                float angle = i * 137.5f * Mathf.Deg2Rad;
                float radius = i == 0 ? 0f : 1.8f + (i / 3) * 0.7f;
                Vector3 desired = _landing + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                NavMeshHit nav;
                Vector3 final = NavMesh.SamplePosition(desired, out nav, 3.0f, NavMesh.AllAreas) ? nav.position : _landing;
                Vector3 away = (final - _landing).sqrMagnitude > 0.01f ? (final - _landing).normalized : YawDirection(_sector.DepartureBearing);
                Quaternion rotation = Quaternion.LookRotation(away, Vector3.up);
                _actors[i].Live.LandingPosition = final;
                _actors[i].Live.LandingRotation = rotation;
                _actors[i].Double.LandingPosition = final;
                _actors[i].Double.LandingRotation = rotation;
            }
        }

        private void PoseCabinActors(float phase, float throwProgress)
        {
            _throwProgress = throwProgress;
            if(PoseUserCabin(phase,throwProgress))return;
            float flightElapsed=Mathf.Max(0f,phase-_flightSceneStart);
            float sourceFlight=Mathf.Min(14.8f,flightElapsed*14.8f/(52.8f/Plugin.FlightSpeedMultiplier.Value));
            float ready=Mathf.Clamp01((sourceFlight-11.4f)/3.4f);
            foreach (ActorSlot actor in _actors)
            {
                actor.Double.SetCabinResponse(_cabinResponse, _aircraft.RootTransform.rotation);
                if (actor.Double.Index == 0)
                {
                    if(_airframe?.SeatedMotion!=null)
                    {
                        // The late native hold has already turned towards its original coil.
                        // Use the upright first sitting segment for our left-side layout; blend
                        // back to its neutral hold before acquisition, not a second conflicting turn.
                        float sourceTime=throwProgress<=0f?Mathf.Min(sourceFlight,8f)
                            :8f*(1-SmootherStep(Mathf.Clamp01(throwProgress/.10f)));
                        actor.Double.ApplyNativeCabinPose(_airframe.SeatedMotion,_aircraft.GetDoorSillSeat(-1),
                            _aircraft.GetDoorSillRotation(-1),sourceTime,true,throwProgress,
                            CabinRopeBundle.EvaluateGrip(_aircraft,throwProgress,_leftGround,0f),
                            CabinRopeBundle.EvaluateGrip(_aircraft,throwProgress,_leftGround,.015f),_aircraft.GetDoorAnchor(-1));
                        continue;
                    }
                    actor.Double.ApplyDoorSillPose(_aircraft.GetDoorSillSeat(-1),
                        _aircraft.GetDoorSillRotation(-1), phase * 0.21f, throwProgress,
                        CabinRopeBundle.EvaluateGrip(_aircraft, throwProgress, _leftGround, 0f),
                        CabinRopeBundle.EvaluateGrip(_aircraft, throwProgress, _leftGround, 0.015f),
                        _aircraft.GetSillRightBrace(-1));
                    continue;
                }
                if (actor.Double.Index == 1)
                {
                    if(_airframe?.GuardMotion!=null)
                    {
                        float sourceTime=throwProgress<=0f?sourceFlight:14.8f+throwProgress*4.0f;
                        actor.Double.ApplyNativeCabinPose(_airframe.GuardMotion,_aircraft.GetDoorGuardPoint(-1),
                            _aircraft.GetDoorGuardRotation(-1),sourceTime,false,throwProgress);
                        continue;
                    }
                    float reaction = Mathf.Sin(Mathf.Clamp01(throwProgress) * Mathf.PI);
                    actor.Double.ApplyDoorGuardPose(_aircraft.GetDoorGuardPoint(-1),
                        _aircraft.GetDoorGuardRotation(-1), phase * 0.19f + 0.7f, reaction);
                    continue;
                }
                if(actor.Double.Index==2 && _useRightRope)
                {
                    actor.Double.ApplyRightDoorKick(_aircraft.GetWaitingFloor(2),_aircraft.GetWaitingRotation(2),
                        _aircraft.GetCabinRopeBundlePoint(1),phase,Mathf.Clamp01((throwProgress-.08f)/.92f));
                    continue;
                }
                actor.Double.ApplyWaitingPose(_aircraft.GetWaitingFloor(actor.Double.Index),
                    _aircraft.GetWaitingRotation(actor.Double.Index), phase,throwProgress>0f?throwProgress:ready);
            }
            if(_actors.Count>1 && ready>.05f)
                for(int i=0;i<_actors.Count;i++)
                {
                    int other=i==0?1:i==1?0:(i+1)%_actors.Count;
                    if(_actors[other].Double.TryGetEyePose(out Vector3 target,out Quaternion unused))
                        _actors[i].Double.LookTowards(target,Mathf.Sin(ready*Mathf.PI)*(i==0?.18f:.65f));
                }
            ApplyCabinAuthoring();
        }

        private void UpdateActorsDuringDescent(float clock, float duration)
        {
            foreach (ActorSlot actor in _actors)
            {
                if (actor.Released) continue;
                actor.Double.SetCabinResponse(_cabinResponse,_aircraft.RootTransform.rotation);
                float local = clock - actor.StartTime;
                if(TryUserPreparation(actor,local))continue;
                Vector3 seat;
                Quaternion seatRotation;
                if (actor.Double.Index == 0)
                {
                    seat = _aircraft.GetDoorSillSeat(-1);
                    seatRotation = _aircraft.GetDoorSillRotation(-1);
                }
                else if (actor.Double.Index == 1)
                {
                    seat = _aircraft.GetDoorGuardPoint(-1);
                    seatRotation = _aircraft.GetDoorGuardRotation(-1);
                }
                else
                {
                    seat = _aircraft.GetWaitingFloor(actor.Double.Index);
                    seatRotation = _aircraft.GetWaitingRotation(actor.Double.Index);
                }
                Vector3 stand = _aircraft.GetDoorStandPoint(actor.Side);
                Quaternion ropeFacing = _aircraft.GetRopeFacing(actor.Side);
                SteelRope rope = actor.Side < 0 ? _leftRope : _rightRope;

                float prep=actor.Double.Index==0?InsertionTiming.MainExit:InsertionTiming.FollowerPreparation;
                if (local < -prep)
                {
                    if(actor.Double.Index<2 && _airframe!=null)
                    {
                        actor.Double.ApplyNativeCabinPose(actor.Double.Index==0?_airframe.SeatedMotion:_airframe.GuardMotion,
                            seat,seatRotation,actor.Double.Index==0?13f:18.6f,actor.Double.Index==0,1f);
                    }
                    else if (actor.Double.Index == 0)
                    {
                        actor.Double.ApplyDoorSillPose(seat, seatRotation,
                            _sceneTime * 0.2f, 0f);
                    }
                    else if (actor.Double.Index == 1)
                    {
                        float watch = 0.18f + Mathf.Sin(_sceneTime * 0.73f) * 0.08f;
                        actor.Double.ApplyDoorGuardPose(seat, seatRotation,
                            _sceneTime * 0.2f + 0.7f, watch);
                    }
                    else
                    {
                        if(actor.Double.Index==2 && _useRightRope)
                            actor.Double.ApplyRightDoorKick(seat,seatRotation,_aircraft.GetCabinRopeBundlePoint(1),_sceneTime,1f);
                        else actor.Double.ApplyWaitingPose(seat,seatRotation,_sceneTime,1f);
                    }
                    continue;
                }
                if (local < 0f)
                {
                    Vector3 firstAxis = rope.EvaluateDescent(0f) - Vector3.up * 0.45f;
                    Vector3 outside = firstAxis - Vector3.up * SupportedRopePose.ContactHeight - ropeFacing * Vector3.forward * DescentBodyOffset;
                    if (actor.Double.Index == 0)
                    {
                        actor.Double.ApplyExitPose(seat, seatRotation, stand, outside, ropeFacing, firstAxis,
                            Mathf.InverseLerp(-prep, 0f, local), 0);
                    }
                    else
                    {
                        Vector3 floor = _aircraft.RootTransform.TransformPoint(new Vector3(
                            _aircraft.RootTransform.InverseTransformPoint(seat).x,
                            _aircraft.CabinFloor,
                            _aircraft.RootTransform.InverseTransformPoint(seat).z));
                        float prepTime = local + prep;
                        Vector3 direction = stand - floor;
                        direction = Vector3.ProjectOnPlane(direction, _aircraft.RootTransform.up);
                        Quaternion walkRotation = direction.sqrMagnitude > 0.01f
                            ? Quaternion.LookRotation(direction.normalized, _aircraft.RootTransform.up) : seatRotation;
                        if(prepTime<InsertionTiming.Rise)
                        {
                            if(actor.Double.Index==2 && _useRightRope)actor.Double.ApplyWalkPose(seat,seatRotation,0f);
                            else actor.Double.ApplyBraceReleasePose(seat,seatRotation,prepTime/InsertionTiming.Rise,actor.Double.Index);
                        }
                        else if(prepTime<InsertionTiming.Rise+InsertionTiming.Walk)
                        {
                            float p=(prepTime-InsertionTiming.Rise)/InsertionTiming.Walk;
                            actor.Double.ApplyCabinWalk(floor,stand,Quaternion.Slerp(seatRotation,walkRotation,
                                SmootherStep(Mathf.Clamp01((prepTime-InsertionTiming.Rise)/.45f))),p);
                        }
                        else
                            actor.Double.ApplyExitPose(stand, walkRotation, stand, outside, ropeFacing, firstAxis,
                                (prepTime-InsertionTiming.Rise-InsertionTiming.Walk)/InsertionTiming.Exit,-1);
                    }
                    continue;
                }
                if (local < duration)
                {
                    if (!actor.SlideSoundStarted)
                    {
                        actor.SlideSoundStarted = true;
                        _effects.BeginSlide();
                    }
                    float raw = Mathf.Clamp01(local / duration);
                    float progress = FastRopeProgress(raw);
                    Vector3 startAxis = rope.EvaluateDescent(0f) - Vector3.up * 0.45f;
                    Vector3 startRoot = startAxis - Vector3.up * SupportedRopePose.ContactHeight - ropeFacing * Vector3.forward * DescentBodyOffset;
                    if (!actor.HasDescentStart) { actor.DescentStartRoot = startRoot; actor.HasDescentStart = true; }
                    Vector3 root = Vector3.Lerp(actor.DescentStartRoot, actor.Live.LandingPosition, progress);
                    if (raw > 0.84f)
                    {
                        float landBlend = SmootherStep((raw - 0.84f) / 0.16f);
                        root = Vector3.Lerp(root, actor.Live.LandingPosition, landBlend);
                    }
                    float orbit = Mathf.Sin(progress * Mathf.PI) * (actor.Double.Index%2==0?5f:-5f);
                    Quaternion rotation = Quaternion.AngleAxis(orbit, Vector3.up) * ropeFacing;
                    Vector3 axis=rope.AtHeight(root.y+SupportedRopePose.ContactHeight);
                    // Body orbits the SAME axis as both palms; it cannot drift on a
                    // separate interpolated XZ path while the hands chase the cable.
                    Vector3 body=axis-rotation*Vector3.forward*DescentBodyOffset;
                    root.x=body.x;root.z=body.z;
                    actor.Double.ApplyRopePose(root, rotation, axis, progress, 1f);
                    continue;
                }

                // Keep the body on the same visible strand through foot contact. No late
                // blend to a separate ground-axis XZ and no lateral hop at the pose switch.
                Vector3 groundAxis = rope.AtHeight(actor.Live.LandingPosition.y+SupportedRopePose.ContactHeight);
                Vector3 grounded=groundAxis-actor.Live.LandingRotation*Vector3.forward*DescentBodyOffset;
                grounded.y=actor.Live.LandingPosition.y;
                actor.Live.LandingPosition=grounded;
                actor.Double.LandingPosition=grounded;
                float touchdown=Mathf.Clamp01((local-duration)/InsertionTiming.Touchdown);
                actor.Double.ApplyTouchdownPose(actor.Live.LandingPosition, actor.Live.LandingRotation, groundAxis,
                    touchdown);
                actor.Double.BlendToGameplayPose(Mathf.InverseLerp(.38f,1f,touchdown));
                if(local>=duration+InsertionTiming.Touchdown)ReleaseActor(actor);
            }
            if (_actors.All(actor => actor.Released || !actor.SlideSoundStarted)) _effects.EndSlide();
        }

        private void ReleaseActor(ActorSlot actor)
        {
            if (actor.Released) return;
            actor.Live.Release(actor.Live.LandingPosition, actor.Live.LandingRotation);
            actor.Double.SetVisible(false);
            actor.Released = true;
            actor.Double.Landed = true;
            _effects.PlayLanding();
            if (actor.Live.IsMain)
            {
                InsertionApi.Land(actor.Live.LandingPosition.x,actor.Live.LandingPosition.y,actor.Live.LandingPosition.z);
                if (!_inspection)
                {
                    EndCameraPresentation();
                }
                Plugin.Log.LogInfo("Main player released immediately at touchdown; remaining lane schedule continues.");
            }
        }

        private void BeginCameraAndInputLock()
        {
            _cameraActive = _camera != null;
            _hasCameraPose = false;
            _lookYaw = 0f;
            _lookPitch = 0f;
            _originalNearClip = _camera.nearClipPlane;
            _fieldOfView?.Dispose();_fieldOfView=new CinematicFieldOfView(_camera);
            _originalCameraMask=_camera.cullingMask;
            _presentationMaskOwned=true;
            _skyCamera = _camera.GetComponent<TOD_Camera>();
            Plugin.Log.LogInfo("Cinematic environment: " + SourceSceneEnvironment.Describe()
                + "; existing TOD observer=" + (_skyCamera != null) + "; HDR=" + _camera.allowHDR);
            _camera.nearClipPlane = 0.035f;
            GamePlayerOwner.SetIgnoreInputInNPCDialog(true);
            _inputLocked = true;
        }

        private void CacheCameraPose()
        {
            ResolveCameraPose();
            ApplyCameraPoseForWorldSystems();
        }

        private void ResolveCameraPose()
        {
            if (_inspection && _aircraft != null)
            {
                _cameraPosition = _aircraft.RootTransform.TransformPoint(_inspectionLocalPosition);
                _cameraRotation = _aircraft.RootTransform.rotation * Quaternion.Euler(_inspectionPitch, _inspectionYaw, 0f);
                _hasCameraPose = true;
                return;
            }
            if (!_cameraActive || _actors.Count == 0 || _actors[0].Double == null) return;
            if (_actors[0].Double.TryGetEyePose(out Vector3 position, out Quaternion headRotation))
            {
                Quaternion freeLook = Quaternion.Euler(_lookPitch, _lookYaw, 0f);
                _cameraPosition = position;
                _cameraRotation = headRotation * freeLook;
                _hasCameraPose = true;
            }
        }

        private void ToggleInspection()
        {
            _inspection = !_inspection;
            if (_inspection)
            {
                _cameraActive = true;
                if(_fieldOfView==null)_fieldOfView=new CinematicFieldOfView(_camera);
                _camera.nearClipPlane = 0.035f;
                _inspectionLocalPosition = _aircraft.RootTransform.InverseTransformPoint(_camera.transform.position);
                var angles = (Quaternion.Inverse(_aircraft.RootTransform.rotation) * _camera.transform.rotation).eulerAngles;
                _inspectionYaw = angles.y;
                _inspectionPitch = Mathf.DeltaAngle(0f, angles.x);
                GamePlayerOwner.SetIgnoreInputInNPCDialog(true); _inputLocked = true;
                _actors[0].Double.SetMainActorFirstPersonPresentation(false);
                if (_visibility == null) _visibility = new CinematicVisibility(_camera);
                if (_environment == null) _environment = new CinematicEnvironment(_world.MainPlayer,_camera);
            }
            else
            {
                _actors[0].Double.SetMainActorFirstPersonPresentation(true);
                if (_actors[0].Released) EndCameraPresentation();
            }
            Plugin.Log.LogInfo("Inspection camera " + (_inspection ? "ON: WASD/QE, mouse, Shift; F8 returns" : "OFF"));
        }

        private void EndCameraPresentation()
        {
            _preparationCover = false; _coverFadeStart = -1f;
            _cameraActive = false; _hasCameraPose = false; _inspection = false;
            _fieldOfView?.Dispose();_fieldOfView=null;
            if (_camera != null && _originalNearClip > 0f) _camera.nearClipPlane = _originalNearClip;
            // Keep presentation visible after player touchdown until the last follower leaves.
            try { _visibility?.Dispose(); }
            finally
            {
                _visibility = null; _skyCamera = null;
                try { _environment?.Dispose(); }
                finally
                {
                    _environment=null;
                    if (_inputLocked) { GamePlayerOwner.SetIgnoreInputInNPCDialog(false); _inputLocked = false; }
                }
            }
        }

        private void UpdateHoverPose(Vector3 inbound)
        {
            // The Tactical Services idle clip owns every hover correction and subtle
            // airframe movement. Mirroring its animated frame avoids double motion.
            _aircraftPosition = _aircraft.RootTransform.position;
            _aircraftRotation = _aircraft.RootTransform.rotation;
            HoldLiveActors();
        }

        private void TickRopes(float leftDeployment, float rightDeployment, float retraction)
        {
            _retraction = retraction;
        }

        private void SimulateRopes()
        {
            Vector3 leftTop = _aircraft.GetDoorAnchor(-1);
            Vector3 rightTop = _aircraft.GetDoorAnchor(1);
            Vector3 wind = YawDirection(_sector.DepartureBearing) * 0.32f;
            // Presentation is a single full-length cable throughout packing/payout. The packed
            // path is imposed below after simulating the eventual suspended span invisibly.
            _leftRope.SetDeployment(1f, _retraction);
            _rightRope.SetDeployment(_useRightRope?1f:0f, _retraction);
            bool packed=_state>=SceneState.Approach && _state<=SceneState.DeployingRopes;
            _leftRope.Tick(leftTop, _leftGround, wind, Time.unscaledDeltaTime,!packed || _cabinRopeBundle==null);
            if(_useRightRope)_rightRope.Tick(rightTop, _rightGround, wind, Time.unscaledDeltaTime,!packed || _rightCabinRopeBundle==null);
        }

        private void HoldLiveActors()
        {
            for (int i = 0; i < _actors.Count; i++)
            {
                if (!_actors[i].Released)
                {
                    _actors[i].Live.HoldOffstage(_offstage);
                }
            }
        }

        private void UpdateEffects(float amount)
        {
            if (_effects == null) return;
            _effects.SetPosition(_landing + Vector3.up * 0.08f);
            _effects.SetRotorWash(amount);
        }

        private IEnumerator RunTimed(float duration, Action<float> update)
        {
            float elapsed = 0f;
            while (elapsed < duration && IsRaidValid())
            {
                elapsed += Time.unscaledDeltaTime;
                update(Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration)));
                yield return null;
            }
        }

        private bool TryFindSafePoint(MapSafeSector sector, out Vector3 point, out string reason)
        {
            point = sector.Seed;
            if (string.Equals(sector.LocationId, "laboratory", StringComparison.OrdinalIgnoreCase))
            {
                reason = "Labs is enclosed; no rotor-safe open-sky volume exists";
                return false;
            }
            if (string.Equals(sector.LocationId, "terminal", StringComparison.OrdinalIgnoreCase))
            {
                reason = "Terminal is database-only in this local SPT build";
                return false;
            }

            string lastLaneReason = "no candidate reached lane validation";
            const int rings = 7;
            for (int ring = 0; ring <= rings; ring++)
            {
                int samples = ring == 0 ? 1 : ring * 8;
                float radius = sector.SearchRadius * ring / rings;
                for (int sample = 0; sample < samples; sample++)
                {
                    float angle = sample / (float)samples * Mathf.PI * 2f + ring * 0.41f;
                    Vector3 probe = sector.Seed + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                    NavMeshHit nav;
                    if (!NavMesh.SamplePosition(probe, out nav, ring == 0 ? 8f : 4.5f, NavMesh.AllAreas)) continue;
                    // Reserve's low-slope barracks roof has NavMesh too. Open sky alone did not
                    // distinguish it from the authored ground-level clearing in test8.1.
                    float groundBand=sector.LocationId=="rezervbase"?1.8f:4f;
                    if(!LandingGeometry.InGroundBand(nav.position,sector.Seed,groundBand))continue;
                    if (!LandingGeometry.IsOpenGround(nav.position)) continue;
                    if (!HasSquadNavArea(nav.position)) continue;
                    if (!HasRotorClearance(nav.position, sector.HoverHeight)) continue;
                    if (!ValidateDoorLanes(nav.position + _hoverLeftOffset, nav.position + _hoverRightOffset,
                        _previewLeftFacing, _previewRightFacing, out lastLaneReason,nav.position.y)) continue;
                    point = nav.position;
                    reason = "NavMesh + squad area + rotor + both actual door corridors clear; offset "
                        + Vector3.Distance(new Vector3(point.x, 0f, point.z), new Vector3(sector.Seed.x, 0f, sector.Seed.z)).ToString("F1") + " m";
                    return true;
                }
            }
            reason = "no clear candidate inside " + sector.SearchRadius.ToString("F0") + " m sector; last lane: " + lastLaneReason;
            return false;
        }

        private bool ValidateDoorLanes(Vector3 left, Vector3 right, Quaternion leftFacing, Quaternion rightFacing, out string reason,float groundY=float.NaN)
        {
            if (!LandingGeometry.TryRopeLanding(left, leftFacing * Vector3.back * DescentBodyOffset, out _leftGround, out reason))
            { reason = "LEFT top=" + left + ": " + reason; return false; }
            float expected=float.IsNaN(groundY)?_landing.y:groundY;
            if(Mathf.Abs(_leftGround.y-expected)>.65f){reason="LEFT lane does not reach the selected ground level";return false;}
            if(!_useRightRope) { _rightGround=_leftGround; return true; }
            if (!LandingGeometry.TryRopeLanding(right, rightFacing * Vector3.back * DescentBodyOffset, out _rightGround, out reason))
            { reason = "RIGHT top=" + right + ": " + reason; return false; }
            if(Mathf.Abs(_rightGround.y-expected)>.65f){reason="RIGHT lane does not reach the selected ground level";return false;}
            return true;
        }

        private static bool HasSquadNavArea(Vector3 center)
        {
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                Vector3 desired = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 2.4f;
                NavMeshHit hit;
                if (!NavMesh.SamplePosition(desired, out hit, 1.5f, NavMesh.AllAreas)) return false;
                if (Mathf.Abs(hit.position.y - center.y) > 1.25f) return false;
                if (!LandingGeometry.IsOpenGround(hit.position)) return false;
            }
            return true;
        }

        private static bool HasRotorClearance(Vector3 ground, float hoverHeight)
        {
            return LandingGeometry.ClearCapsule(ground + Vector3.up * 15f, ground + Vector3.up * 15.01f, 8.6f, out _);
        }

        private void UpdateCabinResponse()
        {
            Vector3 position = _aircraft.RootTransform.position;
            float dt = Mathf.Max(0.001f, Time.unscaledDeltaTime);
            if (!_cabinMotionReady)
            {
                _previousCabinPosition = position; _previousCabinVelocity = Vector3.zero;
                _previousCabinRotation = _aircraft.RootTransform.rotation;
                _filteredCabinAcceleration = Vector3.zero;
                _cabinResponse = Vector3.zero; _cabinMotionReady = true; return;
            }
            Vector3 velocity = (position - _previousCabinPosition) / dt;
            Vector3 acceleration = (velocity - _previousCabinVelocity) / dt;
            _previousCabinPosition = position; _previousCabinVelocity = velocity;
            Transform frame = _aircraft.RootTransform;
            _filteredCabinAcceleration = Vector3.Lerp(_filteredCabinAcceleration,
                Vector3.ClampMagnitude(acceleration, 18f), 1f - Mathf.Exp(-dt * 8f));
            Vector3 local = frame.InverseTransformDirection(_filteredCabinAcceleration);
            Quaternion delta = frame.rotation * Quaternion.Inverse(_previousCabinRotation);
            _previousCabinRotation = frame.rotation;
            delta.ToAngleAxis(out float degrees, out Vector3 axis);
            if (degrees > 180f) degrees -= 360f;
            Vector3 angular = frame.InverseTransformDirection(axis) * (degrees * Mathf.Deg2Rad / dt);
            if (!float.IsNaN(angular.x)) angular = Vector3.ClampMagnitude(angular, 1.2f);
            else angular = Vector3.zero;
            Vector3 gravity = frame.InverseTransformDirection(Vector3.down);
            Vector3 response = new Vector3(gravity.x * 0.115f - local.x * 0.0055f - angular.z * 0.028f,
                -local.y * 0.0015f, gravity.z * 0.10f - local.z * 0.0055f + angular.x * 0.025f);
            _cabinResponse = Vector3.ClampMagnitude(response, 0.095f);
        }

        private bool IsRaidValid()
        {
            return _world != null && _world.MainPlayer != null
                && Comfort.Common.Singleton<GameWorld>.Instantiated
                && Comfort.Common.Singleton<GameWorld>.Instance == _world;
        }

        private void SetState(SceneState state, string status)
        {
            _state = state;
            _stateTime = Time.unscaledTime;
            _status = status;
            Plugin.Log.LogInfo("Insertion state -> " + state + ": " + status);
        }

        private void Fail(string reason)
        {
            SetState(SceneState.Failed, reason);
            Plugin.Log.LogError("UH-60 insertion aborted safely: " + reason);
            Cleanup(false);
            _sequence = null;
        }

        private void Cleanup(bool completed)
        {
            InsertionApi.CancelAttempt(); // Cleanup/restoration is never a touchdown receipt.
            if (_cleaningUp) return;
            _cleaningUp = true;
            try { EndCameraPresentation(); }
            catch (Exception ex) { Plugin.Log.LogError("Camera cleanup failed: " + ex); }
            _throwProgress = 0f; _retraction = 0f;
            if (_inputLocked)
            {
                GamePlayerOwner.SetIgnoreInputInNPCDialog(false);
                _inputLocked = false;
            }
            // Restore every live pawn before destroying visuals. One failed Teleport must not
            // prevent cleanup of the next follower, environment, camera or either cable.
            foreach (ActorSlot actor in _actors)
            {
                DisposeSafely(actor.Live, "live actor"); actor.Live = null;
                DisposeSafely(actor.Double, "visual actor"); actor.Double = null;
            }
            _actors.Clear();
            DisposeSafely(_effects, "audio");
            _effects = null;
            DisposeSafely(_leftRope, "left rope");
            _leftRope = null;
            DisposeSafely(_rightRope, "right rope");
            _rightRope = null;
            DisposeSafely(_cabinRopeBundle, "cabin rope");
            _cabinRopeBundle = null;
            DisposeSafely(_rightCabinRopeBundle,"right cabin rope");_rightCabinRopeBundle=null;
            DisposeSafely(_aircraft, "aircraft");
            _aircraft = null;
            DisposeSafely(_airframe, "source airframe"); _airframe = null;
            if(_presentationMaskOwned && _camera!=null && (_originalCameraMask&(1<<CabinLighting.Layer))==0)
                _camera.cullingMask&=~(1<<CabinLighting.Layer);
            _presentationMaskOwned=false;
            if (completed) Plugin.Log.LogInfo("UH-60 insertion cleanup complete; no global LOD state retained.");
            _cleaningUp = false;
        }

        private static void DisposeSafely(IDisposable resource, string label)
        {
            try { resource?.Dispose(); }
            catch (Exception ex) { Plugin.Log.LogError("Cleanup failed for " + label + ": " + ex); }
        }

        private void OnGUI()
        {
            if (_preparationCover || _coverFadeStart >= 0f)
            {
                float alpha = _preparationCover ? 1f : 1f - Mathf.SmoothStep(0f, 1f,
                    (Time.unscaledTime - _coverFadeStart) / 1.2f);
                if (alpha > 0f)
                {
                    int depth = GUI.depth; Color colour = GUI.color;
                    GUI.depth = -10000; GUI.color = new Color(0f, 0f, 0f, alpha);
                    GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
                    GUI.color = colour; GUI.depth = depth;
                }
                else _coverFadeStart = -1f;
            }
            if (!Plugin.DebugOverlay.Value || _state == SceneState.Idle) return;
            GUI.Box(new Rect(18f, 82f, 480f, 106f), string.Empty);
            GUI.Label(new Rect(30f, 90f, 455f, 24f), "UH-60 FAST-ROPE — " + _state);
            GUI.Label(new Rect(30f, 114f, 455f, 24f), _status);
            GUI.Label(new Rect(30f, 138f, 455f, 24f), "Map: " + (_sector?.LocationId ?? "-") + "  Point: " + _landing.ToString("F1"));
            GUI.Label(new Rect(30f, 162f, 455f, 24f), "Validation: " + _validation);
            if (_state == SceneState.Failed && !string.IsNullOrEmpty(_failureDetail))
            {
                GUI.Box(new Rect(18f, 190f, 780f, 82f), string.Empty);
                GUI.Label(new Rect(30f, 196f, 755f, 70f), _failureDetail);
            }
            if (_sequence != null) GUI.Label(new Rect(30f, 190f, 700f, 24f), _inspection
                ? "INSPECTION: mouse / WASD / Q E / Shift — F8 returns to actor"
                : "F8 — inspect cabin (mouse + WASD/QE), F8 — return");
        }

        private static Vector3 YawDirection(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)).normalized;
        }

        private static float SmootherStep(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * value * (value * (value * 6f - 15f) + 10f);
        }

        private static float FastRopeProgress(float value)
        {
            value = Mathf.Clamp01(value);
            const float acceleration = 0.18f;
            const float deceleration = 0.16f;
            const float speed = 1f / (1f - (acceleration + deceleration) * 0.5f);
            if (value < acceleration)
            {
                return 0.5f * speed * value * value / acceleration;
            }
            if (value > 1f - deceleration)
            {
                float remaining = 1f - value;
                return 1f - 0.5f * speed * remaining * remaining / deceleration;
            }
            return speed * (value - acceleration * 0.5f);
        }
    }
}
