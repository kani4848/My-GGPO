using Cysharp.Threading.Tasks;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class RankingSceneManager : MonoBehaviour, IRankingSceneManager
{
    IEosService eosService;

    [SerializeField] Button globalBtn;
    [SerializeField] Button myRankBtn;
    [SerializeField] Button goLobbyBtn;

    [SerializeField] GameObject LoadingUI;

    [SerializeField] RankingNamePlate namePlatePrefab;
    [SerializeField] GameObject namePlatesRoot;
    List<RankingNamePlate> namePlates = new();

    public RankingSceneState State { get; set; } = RankingSceneState.Global;

    private void Start()
    {
        IGameManager.rankingManager = this;
        LoadingUI.SetActive(true);
        namePlatesRoot.SetActive(false);
        DeactivateButtons();
    }

    private void Update()
    {
        if (!goLobbyBtn.interactable) return;

        if (Input.GetKeyDown(KeyCode.Z) && globalBtn.interactable)
        {
            globalBtn.onClick.Invoke();
            return;
        }

        if (Input.GetKeyDown(KeyCode.X) && myRankBtn.interactable)
        {
            myRankBtn.onClick.Invoke();
            return;
        }

        if (Input.GetKeyDown(KeyCode.C) && goLobbyBtn.interactable)
        {
            goLobbyBtn.onClick.Invoke();
            return;
        }
    }

    public async UniTask Init(IEosService eosService)
    {
        this.eosService = eosService;
        await ShowGlobalRanking();
    }

    public async UniTask ShowGlobalRanking()
    {
        await UpdatePlate(eosService.GetRankingDatas);
    }

    public async UniTask ShowMyRank()
    {
        await UpdatePlate(eosService.GetMyRankingDatas);
    }

    async UniTask UpdatePlate(Func<UniTask<List<RankRow>>> GetRankDataFunc)
    {
        DeactivateButtons();
        namePlatesRoot.SetActive(false);
        LoadingUI.SetActive(true);

        ClearNamePlates();

        var rankRows = await GetRankDataFunc();

        for (int i = 0; i < rankRows.Count; i++)
        {
            var rankData = rankRows[i];
            var namePlate = Instantiate(namePlatePrefab, namePlatesRoot.transform);

            if (rankData.UserId == eosService.GetLocalPlayerData().puid)
            {
                namePlate.Hilight();
            }

            namePlates.Add(namePlate);
            namePlate.SetData(rankRows[i]);
        }

        namePlatesRoot.SetActive(true);
        LoadingUI.SetActive(false);
        ActivateButtons();
    }

    void ClearNamePlates()
    {
        var childCount = namePlatesRoot.GetComponentsInChildren<Transform>();

        for (int i = childCount.Length - 1; i > 0; i--)
        {
            Destroy(childCount[i].gameObject);
        }

        namePlates.Clear();
    }

    void DeactivateButtons()
    {
        globalBtn.interactable = false;
        myRankBtn.interactable = false;
        goLobbyBtn.interactable = false;

        globalBtn.onClick.RemoveAllListeners();
        myRankBtn.onClick.RemoveAllListeners();
        goLobbyBtn.onClick.RemoveAllListeners();
    }

    void ActivateButtons()
    {
        globalBtn.interactable = true;
        myRankBtn.interactable = true;
        goLobbyBtn.interactable = true;

        globalBtn.onClick.AddListener(async() =>
        {
            await ShowGlobalRanking();
        });

        myRankBtn.onClick.AddListener(async () =>
        {
            await ShowMyRank();
        });

        goLobbyBtn.onClick.AddListener(async() =>
        {
            DeactivateButtons();
            await ColorFadeService.Instance.BlackFadeIn();
            State = RankingSceneState.GoLobby;
        });
    }
}
