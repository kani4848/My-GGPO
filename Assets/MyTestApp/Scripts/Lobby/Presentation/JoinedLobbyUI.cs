using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

public class JoinedLobbyUI : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI path;
    [SerializeField] TextMeshProUGUI id;

    [SerializeField] LobbyMemberNamePlate memberNamePlatePrefab;

    [SerializeField] Button readyButton;
    [SerializeField] Button readyCancelButton;
    [SerializeField] Button leaveButton;

    [SerializeField] Transform memberRoot;
    [SerializeField] Transform logRoot;
    [SerializeField] GameObject logPrefab;

    //キーには名前ではなくPUIDを入力
    Dictionary<string, LobbyMemberNamePlate> namePlateDic = new();


    public void Activated(LobbyData lobbyData)
    {
        gameObject.SetActive(true);
        path.text = lobbyData.path == "" ? "undefined" : lobbyData.path;
        id.text = lobbyData.id;

        readyButton.interactable = true;
        leaveButton.interactable = true;

        Debug.Log($"人数は{lobbyData.currentMemberDatas.Count}人");

        foreach (var memberData in lobbyData.currentMemberDatas)
        {
            AddMemberNamePlate(memberData);
        }
    }

    public void Deactivated()
    {
        gameObject.SetActive(false);
        ClearCurrentNamePlates();
        ClearLog();
    }

    void AddMemberNamePlate(PlayerData memberData)
    {
        LobbyMemberNamePlate memberNamePlate = Instantiate(memberNamePlatePrefab, memberRoot);

        memberNamePlate.UpdateImage(memberData);

        namePlateDic.Add(memberData.puid, memberNamePlate);

        // 1) Layoutの計算を即時反映
        LayoutRebuilder.ForceRebuildLayoutImmediate(memberRoot.GetComponent<RectTransform>());
        // 2) 念のためキャンバス全体の更新も確定
        Canvas.ForceUpdateCanvases();
    }

    void ClearCurrentNamePlates()
    {
        foreach(var data in namePlateDic)
        {
            Destroy(data.Value.gameObject);
        }

        namePlateDic.Clear();
    }

    public void SwitchButtonsOnNotReady()
    {
        readyButton.gameObject.SetActive(true); 
        readyCancelButton.gameObject.SetActive(false);
        leaveButton.gameObject.SetActive(true);
    }

    public void DeactivateButtons()
    {
        readyButton.gameObject.SetActive(false);
        readyCancelButton.gameObject.SetActive(false);
        leaveButton.gameObject.SetActive(false);
    }

    public void SwitchButtonsOnReady()
    {
        readyButton.gameObject.SetActive(false);
        readyCancelButton.gameObject.SetActive(true);
        leaveButton.gameObject.SetActive(true);
    }

    //コールバック=========================================================

    public void OnJoined(PlayerData memberData)
    {
        Debug.Log("参加");
        AddMemberNamePlate(memberData);
        CreateLog(memberData, LobbyLogType.JOIN);
    }

    public void OnMemberDataUpdate(PlayerData memberData)
    {
        if (namePlateDic.Count <= 0) return;

        LobbyMemberNamePlate targetNamePlate;

        if(namePlateDic.TryGetValue(memberData.puid, out targetNamePlate)) return;

        targetNamePlate.UpdateImage(memberData);

        var userLogs = logs.Where(m => m.id == memberData.puid);

        foreach(var log in userLogs)
        {
            log.UpdateNameText(memberData.name);
        }
    }

    public void OnReady(PlayerData lobbyMemberData)
    {
        if (namePlateDic.Count == 0) return;

        var targetNamePlate = namePlateDic[lobbyMemberData.puid];
        if (targetNamePlate == null) return;

        targetNamePlate.SetReady(lobbyMemberData.ready);
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

        string userName = remover.name;
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
        if (namePlateDic.Count <= 0) return;
        CreateLog(newOwnerData, LobbyLogType.OWNER_CHANGED);

        foreach (var memberData in namePlateDic)
        {
            bool isNewOwner = memberData.Key == newOwnerData.puid;
            memberData.Value.SetOwner(isNewOwner);
        }

    }

    public void HeartBeat(PlayerData member)
    {
        if (member == null) return;
        
        LobbyMemberNamePlate target;
        bool find = namePlateDic.TryGetValue(member.puid, out target);
        if (!find) return;

        target.HeartBeat();
    }


    //その他=========================================================

    List<LobbyActionLog> logs = new();

    void CreateLog(PlayerData data, LobbyLogType logType)
    {
        var _log = Instantiate(logPrefab, logRoot);
        var log = _log.GetComponent<LobbyActionLog>();
        log.UpdateData(data.puid, data.name, logType);
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
}
