using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using System.IO;

public class LobbyUI_InLobby : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI id;
    [SerializeField] TextMeshProUGUI path;
    [SerializeField] TextMeshProUGUI ping;

    [SerializeField] LobbyMemberNamePlate memberNamePlatePrefab;

    [SerializeField] Button readyButton;
    [SerializeField] Button readyCancelButton;
    [SerializeField] Button leaveButton;

    [SerializeField] Transform memberRoot;
    [SerializeField] Transform logRoot;
    [SerializeField] GameObject logPrefab;

    [SerializeField] GameObject errorWindow;

    //キーには名前ではなくPUIDを入力
    Dictionary<string, LobbyMemberNamePlate> namePlateDic = new();

    bool fleezBtns = false;

    private void Awake()
    {
        //gameObject.SetActive(false);
    }

    public void Activated(LobbyData lobbyData)
    {
        gameObject.SetActive(true);
        path.text = lobbyData.path == "" ? "undefined" : lobbyData.path;
        id.text = lobbyData.id;

        readyButton.interactable = true;
        leaveButton.interactable = true;

        ClearNamePlates();
        ClearLog();

        foreach(var playerData in lobbyData.playerDatas)
        {
            Debug.Log($"インロビー起動によるネームプレート生成");
            Debug.Log($"インロビー起動：{playerData.puid},{playerData.name},{playerData.imageData.charaId},{playerData.imageData.umaCol}");

            UpdateOrCreateNamePlate(playerData);
        }

        ActivatedButtons();
    }

    public void Deactivated()
    {
        gameObject.SetActive(false);
        DeactivateButtons();
        ClearNamePlates();
        ClearLog();
    }

    private void OnDisable()
    {
        DeactivateButtons();
    }

    private void Update()
    {
        if (fleezBtns) return;

        if (Input.GetKeyDown(KeyCode.Z))
        {
            if (readyButton.interactable) readyButton.onClick.Invoke();
            if (readyCancelButton.interactable) readyCancelButton.onClick.Invoke();
        }

        if (Input.GetKeyDown(KeyCode.X))
        {
            if(leaveButton.interactable)leaveButton.onClick.Invoke();
        }
    }

    void ClearNamePlates()
    {
        foreach(var data in namePlateDic)
        {
            Destroy(data.Value.gameObject);
        }

        namePlateDic.Clear();
    }

    public void SwitchButtonsOnReadyCancel()
    {
        readyCancelButton.gameObject.SetActive(false);
        leaveButton.gameObject.SetActive(true);
        readyButton.gameObject.SetActive(true);
    }


    void ActivatedButtons()
    {
        fleezBtns = false;

        readyButton.interactable = true;
        leaveButton.interactable = true;

        readyCancelButton.gameObject.SetActive(false);
        readyCancelButton.interactable = false;

        readyButton.onClick.AddListener(async () =>
        {
            SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);
            LobbyButtonEvent.RaiseReady();

            readyButton.gameObject.SetActive(false);
            readyButton.interactable = false;

            readyCancelButton.gameObject.SetActive(true);
            readyCancelButton.interactable = false;

            leaveButton.interactable = false;

            await UniTask.Delay(TimeSpan.FromSeconds(1));

            readyCancelButton.interactable = true;
        });

        readyCancelButton.onClick.AddListener(async () =>
        {
            SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);
            LobbyButtonEvent.RaiseCancelReady();

            readyCancelButton.gameObject.SetActive(false);
            readyCancelButton.interactable = false;

            readyButton.gameObject.SetActive(true);
            readyButton.interactable = false;

            leaveButton.interactable = true;

            await UniTask.Delay(TimeSpan.FromSeconds(1));

            readyButton.interactable = true;
        });

        leaveButton.onClick.AddListener(() =>
        {
            SoundManager.Instance.PlaySE(SE_Handler.SoundType.BUTTON);
            LobbyButtonEvent.RaiseLeaveLobby();
            DeactivateButtons();
        });
    }

    public void DeactivateButtons()
    {
        fleezBtns = true;

        //readyButton.gameObject.SetActive(false);
        //readyCancelButton.gameObject.SetActive(false);
        //leaveButton.gameObject.SetActive(false);

        readyButton.interactable = false;
        readyCancelButton.interactable = false;
        leaveButton.interactable = false;

        readyButton.onClick.RemoveAllListeners();
        readyCancelButton.onClick.RemoveAllListeners();
        leaveButton.onClick.RemoveAllListeners();
    }

    public void SwitchButtonsOnReady()
    {

        readyButton.interactable = false;
        readyCancelButton.interactable = true;
        leaveButton.interactable = true;

        readyButton.gameObject.SetActive(false);
        leaveButton.gameObject.SetActive(true);
        readyCancelButton.gameObject.SetActive(true);
    }

    //イベントコールバック=========================================================
    public void OnJoined(PlayerData memberData)
    {
        UpdateOrCreateNamePlate(memberData);
        CreateLog(memberData, LobbyLogType.JOIN);
    }

    public void UpdateOrCreateNamePlate(PlayerData memberData)
    {
        LobbyMemberNamePlate namePlate;

        if (!namePlateDic.TryGetValue(memberData.puid, out namePlate))
        {
            namePlate = Instantiate(memberNamePlatePrefab, memberRoot);
            namePlateDic.Add(memberData.puid, namePlate);
        }

        namePlate.UpdateImage(memberData);

        //ログのアップデート
        var userLogs = logs.Where(m => m.id == memberData.puid);

        foreach (var log in userLogs)
        {
            log.UpdateNameText(memberData.name);
        }
    }

    public void OnConnected()
    {
        readyButton.interactable = false;
        leaveButton.interactable = true;
    }

    public void OnLeft(PlayerData memberData)
    {
        Debug.Log("退室");
        var remover = namePlateDic[memberData.puid];

        CreateLog(memberData, LobbyLogType.LEAVE);

        Destroy(remover.gameObject);
        namePlateDic.Remove(memberData.puid);
    }

    public void OnDisconnect(PlayerData memberData)
    {
        var tagetNamePlate = namePlateDic[memberData.puid];
        if (tagetNamePlate == null) return;
        tagetNamePlate.SetDisconnect(true);
        CreateLog(memberData, LobbyLogType.DISCONNECT);
    }

    public void OnRevive(PlayerData memberData)
    {
        var taget = namePlateDic[memberData.puid];
        if (taget == null) return;
        taget.SetDisconnect(false);
        CreateLog(memberData, LobbyLogType.REVIVE);
    }

    public void OnOwnerChanged(PlayerData newOwnerData)
    {
        if (namePlateDic.Count == 0) return;
        CreateLog(newOwnerData, LobbyLogType.OWNER_CHANGED);

        foreach (var memberData in namePlateDic)
        {
            bool isNewOwner = memberData.Key == newOwnerData.puid;
            memberData.Value.SetOwner(isNewOwner);
        }

    }

    public void HeartBeat(PlayerData member)
    {
        LobbyMemberNamePlate target;
        bool find = namePlateDic.TryGetValue(member.puid, out target);
        if (!find) return;

        target.HeartBeat();
    }


    //その他=========================================================

    List<LobbyActionLog> logs = new();

    void CreateLog(PlayerData playerData, LobbyLogType logType)
    {
        Debug.Log($"インロビー起動：{playerData.puid},{playerData.name},{playerData.imageData.charaId},{playerData.imageData.umaCol}");

        var _log = Instantiate(logPrefab, logRoot);
        var log = _log.GetComponent<LobbyActionLog>();
        log.UpdateData(playerData.puid, playerData.name, logType);
        logs.Add(log);
    }

    void ClearLog()
    {
        for (int i = logs.Count - 1; i >= 0; i--)
        {
            Destroy(logs[i].gameObject);
        }

        logs.Clear();
    }

    public void UpdatePing(double val)
    {
        int v = Mathf.CeilToInt((float)val);
        ping.text = $"{v}ms";
    }

    public void SwitchErrorWindow(bool active)
    {
        errorWindow.SetActive(active);
    }
}
