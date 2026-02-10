using UnityEngine;

/// <summary>
/// 継承したクラスを自動的にシングルトン化する基底クラス
/// </summary>
public abstract class Singleton<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T _instance;
    public static T Instance
    {
        get
        {
            if (_instance == null)
            {
                // シーン上から探す
                _instance = FindAnyObjectByType<T>();

                // 見つからなければ自動生成
                if (_instance == null)
                {
                    var go = new GameObject(typeof(T).Name);
                    _instance = go.AddComponent<T>();
                }
            }

            return _instance;
        }
    }

    protected virtual void Awake()
    {
        // 既に別インスタンスがあれば破棄
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this as T;
        DontDestroyOnLoad(_instance.gameObject);
    }
}
