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
  [SerializeField] private InputField roomNameText;
  [SerializeField] private Button callButton;
  [SerializeField] private Button addTracksButton;
  [SerializeField] private Button removeTracksButton;
  [SerializeField] private Camera cam;
  [SerializeField] private InputField infoText;
  [SerializeField] private RawImage RtImage;
#pragma warning restore 0649

  private RTCPeerConnection peerConnection;
  private List<RTCRtpSender> pc1Senders;
  private MediaStream audioStream, videoStream;
  private RTCDataChannel remoteDataChannel;
  private Coroutine sdpCheck;
  private string msg;
  private DelegateOnTrack pc2Ontrack;
  private DelegateOnNegotiationNeeded onNegotiationNeeded;
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
    onNegotiationNeeded = () => { StartCoroutine(PcOnNegotiationNeeded()); };
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
  IEnumerator PcOnNegotiationNeeded()
  {
    Debug.Log("pc1 createOffer start");
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
    if (!videoUpdateStarted)
    {
      StartCoroutine(WebRTC.Update());
      videoUpdateStarted = true;
    }
    addTracksButton.interactable = false;
    removeTracksButton.interactable = true;
  }

  private void RemoveTracks()
  {
    foreach (var sender in pc1Senders)
    {
      peerConnection.RemoveTrack(sender);
    }
    foreach (var sender in pc2Senders)
    {
      _pc2.RemoveTrack(sender);
    }
    pc1Senders.Clear();
    pc2Senders.Clear();
    addTracksButton.interactable = true;
    removeTracksButton.interactable = false;
    trackInfos.Clear();
    infoText.text = "";
  }

  private class JsonMessage
  {
    public string command;
    public JsonMessageDataWithRoom data;
  }

  private class JsonMessageDataWithRoom
  {
    public string room;
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
    audioStream = Audio.CaptureStream();
    videoStream = cam.CaptureStream(1280, 720, 1000000);
    RtImage.texture = cam.targetTexture;
    SendWsMessage("ready", new { room = roomName });
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
    Debug.Log("Created local peer connection object pc1");
    peerConnection.OnIceCandidate = OnIceCandidate;
    peerConnection.OnIceConnectionChange = OnIceConnectionChange;
    // peerConnection.OnNegotiationNeeded = onNegotiationNeeded;
    AddTracks();
    peerConnection.CreateOffer(ref _offerOptions);
  }

  private void SendWsMessage(string cmd, object data)
  {
    var msg = new { command = cmd, data };
    ws.Send(JsonUtility.ToJson(msg));
  }

  private void Call()
  {
    if (roomNameText.text.Length == 0)
    {
      infoText.text = "Please enter a room name";
      return;
    }
    callButton.interactable = false;
    roomName = roomNameText.text;
    ws = new WebsocketClient(HandleMessage);
    ws.Connect();
    SendWsMessage("create or join", new { room = roomName });

    /*
    Debug.Log("GetSelectedSdpSemantics");
    var configuration = GetSelectedSdpSemantics();
    _pc1 = new RTCPeerConnection(ref configuration);
    Debug.Log("Created local peer connection object pc1");
    _pc1.OnIceCandidate = pc1OnIceCandidate;
    _pc1.OnIceConnectionChange = pc1OnIceConnectionChange;
    _pc1.OnNegotiationNeeded = pc1OnNegotiationNeeded;
    // _pc2 = new RTCPeerConnection(ref configuration);
    // Debug.Log("Created remote peer connection object pc2");
    // _pc2.OnIceCandidate = pc2OnIceCandidate;
    // _pc2.OnIceConnectionChange = pc2OnIceConnectionChange;
    // _pc2.OnTrack = pc2Ontrack;

    // RTCDataChannelInit conf = new RTCDataChannelInit(true);
    // _pc1.CreateDataChannel("data", ref conf);
    audioStream = Audio.CaptureStream();
    videoStream = cam.CaptureStream(1280, 720, 1000000);
    RtImage.texture = cam.targetTexture;
    */
  }

  private void OnIceCandidate(RTCIceCandidate​ rtcCandidate)
  {
    // GetOtherPc(pc).AddIceCandidate(ref candidate);
    Debug.Log($"Local ICE candidate:\n {rtcCandidate.candidate}");
    SendWsMessage("candidate", new {
      room = roomName,
      label = rtcCandidate.sdpMLineIndex,
      id = rtcCandidate.sdpMid,
      rtcCandidate.candidate
    });
  }

  private void OnTrack(RTCPeerConnection pc, RTCTrackEvent e)
  {
    pc2Senders.Add(pc.AddTrack(e.Track, videoStream));
    trackInfos.Append($"{GetName(pc)} receives remote track:\r\n");
    trackInfos.Append($"Track kind: {e.Track.Kind}\r\n");
    trackInfos.Append($"Track id: {e.Track.Id}\r\n");
    infoText.text = trackInfos.ToString();
  }

  private string GetName(RTCPeerConnection pc)
  {
    return (pc == peerConnection) ? "pc1" : "pc2";
  }

  private RTCPeerConnection GetOtherPc(RTCPeerConnection pc)
  {
    return (pc == peerConnection) ? _pc2 : peerConnection;
  }

  private IEnumerator OnCreateOfferSuccess(RTCSessionDescription desc)
  {
    Debug.Log($"Offer from pc\n{desc.sdp}");
    Debug.Log("pc setLocalDescription start");
    var op = peerConnection.SetLocalDescription(ref desc);
    yield return op;

    if (!op.IsError)
    {
      OnSetLocalSuccess(peerConnection);
    }
    else
    {
      var error = op.Error;
      OnSetSessionDescriptionError(ref error);
    }

  }

  private void foo()
  {
    Debug.Log("pc2 setRemoteDescription start");
    var op2 = _pc2.SetRemoteDescription(ref desc);
    yield return op2;
    if (!op2.IsError)
    {
      OnSetRemoteSuccess(_pc2);
    }
    else
    {
      var error = op2.Error;
      OnSetSessionDescriptionError(ref error);
    }
    Debug.Log("pc2 createAnswer start");
    // Since the 'remote' side has no media stream we need
    // to pass in the right constraints in order for it to
    // accept the incoming offer of audio and video.

    var op3 = _pc2.CreateAnswer(ref _answerOptions);
    yield return op3;
    if (!op3.IsError)
    {
      yield return OnCreateAnswerSuccess(op3.Desc);
    }
    else
    {
      OnCreateSessionDescriptionError(op3.Error);
    }
  }

  private void OnAudioFilterRead(float[] data, int channels)
  {
    Audio.Update(data, data.Length);
  }

  private void OnSetLocalSuccess(RTCPeerConnection pc)
  {
    Debug.Log($"{GetName(pc)} SetLocalDescription complete");
  }

  static void OnSetSessionDescriptionError(ref RTCError error)
  {
    Debug.LogError($"Error Detail Type: {error.message}");
  }

  private void OnSetRemoteSuccess(RTCPeerConnection pc)
  {
    Debug.Log($"{GetName(pc)} SetRemoteDescription complete");
  }

  IEnumerator OnCreateAnswerSuccess(RTCSessionDescription desc)
  {
    Debug.Log($"Answer from pc2:\n{desc.sdp}");
    Debug.Log("pc2 setLocalDescription start");
    var op = _pc2.SetLocalDescription(ref desc);
    yield return op;

    if (!op.IsError)
    {
      OnSetLocalSuccess(_pc2);
    }
    else
    {
      var error = op.Error;
      OnSetSessionDescriptionError(ref error);
    }

    Debug.Log("pc1 setRemoteDescription start");

    var op2 = peerConnection.SetRemoteDescription(ref desc);
    yield return op2;
    if (!op2.IsError)
    {
      OnSetRemoteSuccess(peerConnection);
    }
    else
    {
      var error = op2.Error;
      OnSetSessionDescriptionError(ref error);
    }
  }

  private static void OnCreateSessionDescriptionError(RTCError error)
  {
    Debug.LogError($"Error Detail Type: {error.message}");
  }
}
