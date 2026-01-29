using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System;
using PlayEveryWare.EpicOnlineServices.Samples;
using NUnit.Framework;
using System.Collections.Generic;
using Epic.OnlineServices;
using Unity.VisualScripting;
using Unity.PlasticSCM.Editor.WebApi;
using System.Linq;

public class LobbyMemberData
{
    public string userName;
    public ProductUserId puid;
    public bool isOwner = false;

    public LobbyMemberDataDisplay displayUI;

    public LobbyMemberData(ProductUserId _puid, LobbyMemberDataDisplay _displayUI)
    {
        puid = _puid;
        displayUI = _displayUI;
    }
}

public class JoinedLobbyUI : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI path;
    [SerializeField] TextMeshProUGUI id;

    [SerializeField] LobbyMemberDataDisplay lobbyMemberDisplayPrefab;

    [SerializeField] Button leaveButton;

    [SerializeField] Transform memberRoot;
    [SerializeField] Transform logRoot;
    [SerializeField] TextMeshProUGUI logText;

    List<LobbyMemberData> currentMemberDatas = new();

    ProductUserId _prevOwner;

    public void Activated(string lobbyPath, string lobbyId, List<ProductUserId> puids)
    {
        gameObject.SetActive(true);
        path.text = lobbyPath == "" ? "undefined" : lobbyPath;
        id.text = lobbyId;

        Debug.Log("ロビー起動");

        foreach(ProductUserId puid in puids)
        {
            AddMemberData(puid);
        }
    }

    public void Deactivated()
    {
        _prevOwner = null;
        gameObject.SetActive(false);
        ClearCurrentMemberDatas();
        ClearLog();
    }

    void AddMemberData(ProductUserId puid)
    {
        var a = currentMemberDatas.Find(m=>m.puid == puid);
        if (a != null) return;

        LobbyMemberDataDisplay memberDisplay = Instantiate(lobbyMemberDisplayPrefab, memberRoot);
        memberDisplay.SetInfo(puid.ToString());
        currentMemberDatas.Add(new LobbyMemberData(puid, memberDisplay));
    }

    void ClearCurrentMemberDatas()
    {
        foreach(var data in currentMemberDatas)
        {
            Destroy(data.displayUI.gameObject);
        }

        currentMemberDatas.Clear();
    }

    void ClearLog()
    {
        for (int i = logRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(logRoot.GetChild(i).gameObject);
        }
    }

    public void OnJoined(ProductUserId puid)
    {
        Debug.Log("参加");
        AddMemberData(puid);
    }

    public void OnUserNameApplied(ProductUserId puid, string userName)
    {
        Debug.Log("ユーザー名設定");
        Debug.Log(userName);

        if (userName =="")userName = LobbySceneManager.emptyPlayerName;
        var log = Instantiate(logText, logRoot);
        log.text = $"{userName} enter the lobby.";

        var member = currentMemberDatas.First(m => m.displayUI.puid.text == puid.ToString());
        member.displayUI.SetUserName(userName);
    }

    public void OnLeft(ProductUserId puid)
    {
        Debug.Log("退室");
        var remover = currentMemberDatas.Find(m => m.puid == puid);

        string userName = remover.userName;
        if (userName == "") userName = LobbySceneManager.emptyPlayerName;
        var log = Instantiate(logText, logRoot);
        log.text = $"{userName} leave the lobby.";

        Destroy(remover.displayUI);
        currentMemberDatas.Remove(remover);
    }

    public void OnOwnerChanged(ProductUserId puid)
    {
        if (puid == _prevOwner) return;

        var newOwner = currentMemberDatas.FirstOrDefault(m => m.puid == puid);
        newOwner.displayUI.SetOwner();
        newOwner.displayUI.transform.SetAsFirstSibling();

        _prevOwner = puid;
    }
}
