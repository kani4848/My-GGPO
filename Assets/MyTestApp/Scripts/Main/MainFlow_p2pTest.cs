using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor.UI;
using UnityEngine;

public interface INetTransport
{
    // 自分が送る（相手に届く想定）
    void Send(byte[] payload);

    // 受信キュー（届いた順に取り出す）
    bool TryDequeue(out byte[] payload);
}

/// <summary>
/// ローカル2P疑似通信（EOS差し替え前の動作確認用）
/// </summary>
public sealed class LoopbackTransport : INetTransport
{
    private readonly Queue<byte[]> _rx = new Queue<byte[]>();
    private readonly LoopbackTransport _peer;

    public LoopbackTransport(LoopbackTransport peer)
    {
        _peer = peer;
    }

    public void Send(byte[] payload)
    {
        // 相手の受信キューに突っ込む
        _peer._rx.Enqueue(payload);
    }

    public bool TryDequeue(out byte[] payload)
    {
        if (_rx.Count > 0)
        {
            payload = _rx.Dequeue();
            return true;
        }
        payload = null;
        return false;
    }
}

public sealed class MainFlow_p2pTest
{
    [Header("Test Config")]
    [SerializeField] private bool useLoopback = true;

    [Header("Lockstep")]
    [SerializeField] private int inputDelayFrames = 3;

    [Header("Keys")]
    [SerializeField] private KeyCode p1Key = KeyCode.Space;
    [SerializeField] private KeyCode p2Key = KeyCode.Return;

    // 自分視点でのネット
    private INetTransport _net;

    // ループバック用（片方を相手扱いにするだけ）
    private INetTransport _netPeer;

    // 入力バッファ：frame -> pressed
    private readonly Dictionary<int, bool> _localInputDic = new();
    private readonly Dictionary<int, bool> _remoteInputDic = new();

    private uint _seed;

    public bool isOwner = true;
    public uint seed;
    public int delayFrame;

    int localPressedFrame = -1;
    int remotePressedFrame = -1;

    //初期化ステート================================
    public void Init()
    {
        if (useLoopback)
        {
            // 1台でP1/P2を動かすための疑似2端末
            var a = new LoopbackTransport(null);
            var b = new LoopbackTransport(a);

            // aのpeerをbにする（相互参照を作る）
            // ※LoopbackTransportの設計都合で、ここだけ反射的に差し替えるより、
            //   2つのインスタンスを作る形にしている
            // ここでは「aが自分」「bが相手」として扱う
            typeof(LoopbackTransport)
                .GetField("_peer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(a, b);

            _net = a;
            _netPeer = b;

            // ループバックの場合、相手側も同じStartを受けて開始する想定なので、
            // このサンプルでは相手側の入力もローカルで生成して送る（Returnキー）
        }
        else
        {
            // TODO: ここをEOS P2P実装に差し替える（INetTransportを実装）
            Debug.LogError("INetTransport is not set. Replace with EOS P2P transport.");
        }
    }

    public void Init_Online()
    {

    }

    //オーナーならシード値を生成
    public uint CreateAndSendSeed()
    {
        // ownerがseedを決めて送る（ロビーowner想定）
        _seed = (uint)UnityEngine.Random.Range(1, int.MaxValue);
        _net.Send(NetMessage.PackStart(_seed, inputDelayFrames));

        if (useLoopback)
        {
            // ループバック相手にも開始通知を届ける（同一プロセス内）
            _netPeer.Send(NetMessage.PackStart(_seed, inputDelayFrames));
        }

        return _seed;
    }

    public async UniTask<bool> WaitForSeedReply()
    {
        while (true)
        {
            while (_net.TryDequeue(out var payload))
            {
                var type = NetMessage.PeekType(payload);

                if (type == NetMessage.MsgType.Start)
                {
                    var msg = NetMessage.UnpackStart(payload);

                    //Debug.Log($"{msg.seed}");

                    return true;
                }
            }
            await UniTask.Yield();
        }
    }

    //オーナーでなければシード値を受け取るまで待機
    public async UniTask<uint> WaitForRecievingSeed()
    {
        while (true)
        {
            while (_net.TryDequeue(out var payload))
            {
                var type = NetMessage.PeekType(payload);

                if (type == NetMessage.MsgType.Start)
                {
                    var msg = NetMessage.UnpackStart(payload);
                    inputDelayFrames = Mathf.Max(0, msg.inputDelayFrames);
                    seed = msg.seed;
                    delayFrame = msg.inputDelayFrames;

                    return seed;
                }
            }

            await UniTask.Yield();
        }
    }

    //ラウンド準備ステート================================
    public void SendRoundReadyMsg()
    {
        _net.Send(NetMessage.PackReady());

        //検証用なら自分にも送る
        if (useLoopback)
        {
            _netPeer.Send(NetMessage.PackReady());
        }
    }

    public async UniTask<bool> WaitAndRecieveReady()
    {
        while (true)
        {
            while (_net.TryDequeue(out var payload))
            {
                var type = NetMessage.PeekType(payload);

                if (type == NetMessage.MsgType.Ready)
                {
                    //var msg = NetMessage.UnpackStart(payload);
                    return true;
                }
            }

            await UniTask.Yield();
        }
    }


    public bool RoundSetUpLoop()
    {
        while (_net.TryDequeue(out var payload))
        {
            var type = NetMessage.PeekType(payload);

            Debug.Log(type);

            if (type == NetMessage.MsgType.Ready)
            {
                return true;
            }
        }

        return false;
    }


    //メインゲームステート================================
    // 毎フレーム「ローカル入力」を送信、保存
    public bool SendAndSaveLocalInput(int currentFrame)
    {
        bool _localPressed = Input.GetKeyDown(p1Key);

        if (localPressedFrame == -1 && _localPressed)
        {
            localPressedFrame = currentFrame;
            _localInputDic[currentFrame] = true;
        }
        else
        {
            _localPressed = false;
            _localInputDic[currentFrame] = false;
        }

        _net.Send(NetMessage.PackInput(currentFrame, _localPressed));

        //ローカル検証用に相手データも生成して送る（Returnキーを「相手ボタン」扱い）
        if (useLoopback)
        {
            SendLoopBack();
        }

        return _localPressed;

        void SendLoopBack()
        {
            bool peerPressed = Input.GetKeyDown(p2Key);

            if (remotePressedFrame == -1 && peerPressed)
            {
                remotePressedFrame = currentFrame;
            }
            else
            {
                peerPressed = false;
            }

            _netPeer.Send(NetMessage.PackInput(currentFrame, peerPressed));
        }
    }

    //相手の入力情報の受信と保存
    public bool RecieveAndSaveRemoteInput() 
    {
        bool remotePressed = false;

        while (_net.TryDequeue(out var payload))
        {
            //payloadの先頭1バイトで何のデータか識別
            var type = NetMessage.PeekType(payload);

            if (type == NetMessage.MsgType.Input)
            {
                var msg = NetMessage.UnpackInput(payload);
                remotePressed = msg.pressed != 0;
                _remoteInputDic[msg.frame] = remotePressed;
            }
        }

        return remotePressed;
    }


    //リザルトステート================================
    public (int local, int remote) GetBothInput()
    {
        return (localPressedFrame, remotePressedFrame);
    }

    public void OnRoundReset()
    {
        _localInputDic.Clear();
        _remoteInputDic.Clear();
        localPressedFrame = -1;
        remotePressedFrame = -1;
    }
}
