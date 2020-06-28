using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(AudioListener))]
public class MediaStreamSample : MonoBehaviour
{
#pragma warning disable 0649
  [SerializeField] private Button callButton;
  [SerializeField] private Button addTracksButton;
  [SerializeField] private Button removeTracksButton;
  [SerializeField] private Camera cam;
  [SerializeField] private InputField infoText;
  [SerializeField] private InputField roomNameText;
  [SerializeField] private RawImage RtImage;
#pragma warning restore 0649

  private RTCPeerConnection peerConnection;
  private List<RTCRtpSender> pc1Senders;
  private MediaStream audioStream, videoStream;
  private StringBuilder trackInfos;
  private bool videoUpdateStarted;
  private bool isCaller;
  private string roomName;
  private WebsocketClient ws;

  private RTCOfferOptions _offerOptions = new RTCOfferOptions
  {
    iceRestart = false,
    offerToReceiveAudio = true,
    offerToReceiveVideo = true
  };

  private RTCAnswerOptions _answerOptions = new RTCAnswerOptions
  {
    iceRestart = false,
  };

  private void Awake()
  {
    WebRTC.Initialize();
    callButton.onClick.AddListener(Call);
    addTracksButton.onClick.AddListener(AddTracks);
    removeTracksButton.onClick.AddListener(RemoveTracks);
  }

  private void OnDestroy()
  {
    Audio.Stop();
    WebRTC.Dispose();
  }

  private void Start()
  {
    trackInfos = new StringBuilder();
    pc1Senders = new List<RTCRtpSender>();
    callButton.interactable = true;

    // pc2Ontrack = e => { OnTrack(_pc2, e); };
    infoText.text = !WebRTC.SupportHardwareEncoder ? "Current GPU doesn't support encoder" : "Current GPU supports encoder";
  }

  private static RTCConfiguration GetSelectedSdpSemantics()
  {
    RTCConfiguration config = default;
    config.iceServers = new[]
    {
      new RTCIceServer { urls = new[] { "stun:stun.services.mozilla.com", "stun:stun.l.google.com:19302" } }
    };

    return config;
  }

  private void OnIceConnectionChange(RTCIceConnectionState state)
  {
    switch (state)
    {
      case RTCIceConnectionState.New:
        Debug.Log($"Local IceConnectionState: New");
        break;
      case RTCIceConnectionState.Checking:
        Debug.Log($"Local IceConnectionState: Checking");
        break;
      case RTCIceConnectionState.Closed:
        Debug.Log($"Local IceConnectionState: Closed");
        break;
      case RTCIceConnectionState.Completed:
        Debug.Log($"Local IceConnectionState: Completed");
        break;
      case RTCIceConnectionState.Connected:
        Debug.Log($"Local IceConnectionState: Connected");
        break;
      case RTCIceConnectionState.Disconnected:
        Debug.Log($"Local IceConnectionState: Disconnected");
        break;
      case RTCIceConnectionState.Failed:
        Debug.Log($"Local IceConnectionState: Failed");
        break;
      case RTCIceConnectionState.Max:
        Debug.Log($"Local IceConnectionState: Max");
        break;
      default:
        throw new ArgumentOutOfRangeException(nameof(state), state, null);
    }
  }
  IEnumerator CreateOffer()
  {
    Debug.Log("createOffer start");
    var op = peerConnection.CreateOffer(ref _offerOptions);
    yield return op;

    if (!op.IsError)
    {
      yield return StartCoroutine(OnCreateOfferSuccess(op.Desc));
    }
    else
    {
      OnCreateSessionDescriptionError(op.Error);
    }
  }

  IEnumerator HandleOffer(string sdp)
  {
    Debug.Log("HandleOffer start");
    Debug.Log("setRemoteDescription start");
    var desc = new RTCSessionDescription { sdp = sdp, type = RTCSdpType.Offer };
    var op = peerConnection.SetRemoteDescription(ref desc);
    yield return op;

    if (!op.IsError)
    {
      OnSetRemoteSuccess();
      yield return StartCoroutine(CreateAnswer());
    }
    else
    {
      var error = op.Error;
      OnSetSessionDescriptionError(ref error);
    }
  }

  IEnumerator CreateAnswer()
  {
    Debug.Log("CreateAnswer start");
    var op = peerConnection.CreateAnswer(ref _answerOptions);
    yield return op;

    if (!op.IsError)
    {
      yield return StartCoroutine(OnCreateAnswerSuccess(op.Desc));
    }
    else
    {
      OnCreateSessionDescriptionError(op.Error);
    }
  }

  private void AddTracks()
  {
    foreach (var track in audioStream.GetTracks())
    {
      pc1Senders.Add(peerConnection.AddTrack(track, audioStream));
    }
    foreach (var track in videoStream.GetTracks())
    {
      pc1Senders.Add(peerConnection.AddTrack(track, videoStream));
    }
    /*
    if (!videoUpdateStarted)
    {
      StartCoroutine(WebRTC.Update());
      videoUpdateStarted = true;
    }
    */
  }

  private void RemoveTracks()
  {
    foreach (var sender in pc1Senders)
    {
      peerConnection.RemoveTrack(sender);
    }

    pc1Senders.Clear();
    addTracksButton.interactable = true;
    removeTracksButton.interactable = false;
    trackInfos.Clear();
    infoText.text = "";
  }

  [Serializable]
  public class JsonMessage
  {
    public string command;
    public JsonMessageData data;
  }

  [Serializable]
  public class JsonMessageData
  {
    public string room;
    public string sdp;
    public int label;
    public string id;
    public string candidate;
  }

  private void HandleMessage(string message)
  {
    JsonMessage jsonMsg = JsonUtility.FromJson<JsonMessage>(message);
    switch (jsonMsg.command)
    {
      case "created":
        HandleCreated(jsonMsg.data.room);
        break;
      case "joined":
        HandleJoined(jsonMsg.data.room);
        break;
      case "full":
        HandleFull(jsonMsg.data.room);
        break;
      case "ready":
        HandleReady(jsonMsg.data.room);
        break;
      case "offer":
        HandleOffer(jsonMsg.data.room, jsonMsg.data.sdp);
        break;
      case "answer":
        HandleAnswer(jsonMsg.data.room, jsonMsg.data.sdp);
        break;
      case "candidate":
        HandleCandidate(jsonMsg.data.room, jsonMsg.data);
        break;
    }
  }

  private void HandleCreated(string room)
  {
    Debug.Log("HandleCreated");
    if (room != roomName)
    {
      Debug.LogError("Wrong room name " + room);
      return;
    }

    isCaller = true;
    PrepareMedia();
  }

  private void PrepareMedia()
  {
    audioStream = Audio.CaptureStream();
    videoStream = cam.CaptureStream(1280, 720, 1000000);
    RtImage.texture = cam.targetTexture;
  }

  private void HandleJoined(string room)
  {
    Debug.Log("HandleJoined");
    if (room != roomName)
    {
      Debug.LogError("Wrong room name " + room);
      return;
    }

    isCaller = false;
    PrepareMedia();
    SendWsMessage("ready", new JsonMessageData{ room = roomName });
  }

  private void HandleFull(string room)
  {
    Debug.Log("HandleFull");
    if (room != roomName)
    {
      Debug.LogError("Wrong room name " + room);
      return;
    }

    infoText.text = "The room is full";
  }

  private void HandleReady(string room)
  {
    Debug.Log("HandleReady");
    if (room != roomName)
    {
      Debug.LogError("Wrong room name " + room);
      return;
    }
    if (!isCaller)
    {
      Debug.LogWarning("Not a caller, can't handle 'ready'");
      return;
    }

    Debug.Log("GetSelectedSdpSemantics");
    var configuration = GetSelectedSdpSemantics();
    peerConnection = new RTCPeerConnection(ref configuration);
    Debug.Log("Created local peer connection");
    peerConnection.OnIceCandidate = OnIceCandidate;
    peerConnection.OnIceConnectionChange = OnIceConnectionChange;
    peerConnection.OnTrack = e => OnTrack(e);
    AddTracks();
    StartCoroutine(CreateOffer());
  }

  private void HandleOffer(string room, string sdp)
  {
    Debug.Log("HandleOffer");
    if (room != roomName)
    {
      Debug.LogError("Wrong room name " + room);
      return;
    }
    if (isCaller)
    {
      Debug.LogWarning("Is caller, can't handle 'offer'");
      return;
    }

    Debug.Log($"Received offer: {sdp}");

    Debug.Log("GetSelectedSdpSemantics");
    var configuration = GetSelectedSdpSemantics();
    peerConnection = new RTCPeerConnection(ref configuration);
    Debug.Log("Created local peer connection");
    peerConnection.OnIceCandidate = OnIceCandidate;
    peerConnection.OnIceConnectionChange = OnIceConnectionChange;
    AddTracks();
    StartCoroutine(HandleOffer(sdp));
  }

  private void HandleAnswer(string room, string sdp)
  {
    Debug.Log("HandleAnswer");
    if (room != roomName)
    {
      Debug.LogError("Wrong room name " + room);
      return;
    }
    if (!isCaller)
    {
      Debug.LogWarning("Not a caller, can't handle 'answer'");
      return;
    }
    RTCSessionDescription desc = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = sdp };
    StartCoroutine(HandleAnswer(desc));
  }

  private void HandleCandidate(string room, JsonMessageData data)
  {
    Debug.Log("HandleCandidate");
    if (room != roomName)
    {
      Debug.LogError("Wrong room name " + room);
      return;
    }
    if (data.candidate == null || data.candidate.Length == 0)
    {
      Debug.LogError("Empty candidate");
      return;
    }

    var candidate = new RTCIceCandidate
    {
      candidate = data.candidate,
      sdpMid = data.id,
      sdpMLineIndex = (int)data.label
    };

    peerConnection.AddIceCandidate(ref candidate);
  }

  IEnumerator HandleAnswer(RTCSessionDescription desc)
  {
    Debug.Log($"HandleAnswer\n{desc.sdp}");
    Debug.Log("setRemoteDescription start");
    var op = peerConnection.SetRemoteDescription(ref desc);
    yield return op;

    if (!op.IsError)
    {
      OnSetRemoteSuccess();
    }
    else
    {
      var error = op.Error;
      OnSetSessionDescriptionError(ref error);
    }
  }

  private void SendWsMessage(string cmd, JsonMessageData data)
  {
    var msg = new JsonMessage{ command = cmd, data = data };
    var msgStr = JsonUtility.ToJson(msg);
    ws.Send(msgStr);
  }

  private void Call()
  {
    roomName = roomNameText.text;
    Debug.Log($"Calling room {roomName}");
    if (roomName.Length == 0)
    {
      infoText.text = "Please enter a room name";
      return;
    }
    callButton.interactable = false;
    void onOpen()
    {
      Debug.Log("onOpen");
      SendWsMessage("create or join", new JsonMessageData{ room = roomName });
    }
    void onMessage(string msg)
    {
      Debug.Log("onMessage");
      HandleMessage(msg);
    }
    ws = new WebsocketClient(onOpen, onMessage);
    ws.Connect();
  }
  
  private void OnIceCandidate(RTCIceCandidate​ rtcCandidate)
  {
    // GetOtherPc(pc).AddIceCandidate(ref candidate);
    Debug.Log($"Local ICE candidate:\n {rtcCandidate.candidate}");
    SendWsMessage("candidate", new JsonMessageData{
      room = roomName,
      label = rtcCandidate.sdpMLineIndex,
      id = rtcCandidate.sdpMid,
      candidate = rtcCandidate.candidate
    });
  }
  private void OnTrack(RTCTrackEvent e)
  {
    // pc2Senders.Add(pc.AddTrack(e.Track, videoStream));
    trackInfos.Append($"received remote track:\r\n");
    trackInfos.Append($"Track kind: {e.Track.Kind}\r\n");
    trackInfos.Append($"Track id: {e.Track.Id}\r\n");
    infoText.text = trackInfos.ToString();
  }

  private IEnumerator OnCreateOfferSuccess(RTCSessionDescription desc)
  {
    Debug.Log("OnCreateOfferSuccess");
    Debug.Log($"Offer from pc\n{desc.sdp}");
    Debug.Log("pc setLocalDescription start");
    var op = peerConnection.SetLocalDescription(ref desc);
    yield return op;

    if (!op.IsError)
    {
      OnSetLocalSuccess(desc, true);
    }
    else
    {
      var error = op.Error;
      OnSetSessionDescriptionError(ref error);
    }
  }

  private void OnAudioFilterRead(float[] data, int channels)
  {
    Audio.Update(data, data.Length);
  }

  private void OnSetLocalSuccess(RTCSessionDescription desc, bool isOffer)
  {
    var cmd = isOffer ? "offer" : "answer";
    Debug.Log("SetLocalDescription complete");
    SendWsMessage(cmd, new JsonMessageData{ sdp = desc.sdp, room = roomName });
  }

  static void OnSetSessionDescriptionError(ref RTCError error)
  {
    Debug.LogError($"Error Detail Type: {error.message}");
  }

  private void OnSetRemoteSuccess()
  {
    Debug.Log("SetRemoteDescription complete");
  }

  IEnumerator OnCreateAnswerSuccess(RTCSessionDescription desc)
  {
    Debug.Log($"Answer created:\n{desc.sdp}");
    Debug.Log("setLocalDescription start");
    var op = peerConnection.SetLocalDescription(ref desc);
    yield return op;

    if (!op.IsError)
    {
      OnSetLocalSuccess(desc, false);
    }
    else
    {
      var error = op.Error;
      OnSetSessionDescriptionError(ref error);
    }
  }

  private static void OnCreateSessionDescriptionError(RTCError error)
  {
    Debug.LogError($"Error Detail Type: {error.message}");
  }

  private void OnApplicationQuit()
  {
    ws?.Close();
  }

  void Update()
  {
    #if !UNITY_WEBGL || UNITY_EDITOR
      ws?.Dispatch();
    #endif
  }


}
