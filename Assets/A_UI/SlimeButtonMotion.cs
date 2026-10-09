using UnityEngine;
using DG.Tweening;
using System.Collections.Generic;
[RequireComponent(typeof(RectTransform))]
public class SlimeButtonMotion : MonoBehaviour
{
    private RectTransform rectTransform;
    private readonly Dictionary<GameObject, Tween> motionTweens = new Dictionary<GameObject, Tween>();
    private Vector2 originalPosition;
    private Vector2 otherPosition;

    [Header("按钮组")]
    public GameObject Button_1; // 主体按钮
    public GameObject Button_2; // 辅助按钮
    public GameObject Button_3; // 辅助按钮
    public GameObject Button_4; // 辅助按钮



    [SerializeField]
    protected float motionDuration = 0.3f;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rectTransform = Button_1.GetComponent<RectTransform>();
        originalPosition = rectTransform.anchoredPosition;
        otherPosition = originalPosition + new Vector2(0, -100);
        Button_2.SetActive(false);
        Button_3.SetActive(false);
        Button_4.SetActive(false);
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha0))
        {
            Button_2.SetActive(true);

            LinearMotion(new List<GameObject> { Button_2, Button_4 }, otherPosition);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            Button_3.SetActive(true);

            var targetPos1 = originalPosition + new Vector2(50, 0);
            var targetPos2 = originalPosition + new Vector2(-50, 0);

            LinearMotion(Button_3, targetPos1);
            LinearMotion(Button_1, targetPos2);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            Button_4.SetActive(true);

            LinearMotion(Button_4, otherPosition + new Vector2(50, 0));
            LinearMotion(Button_2, otherPosition + new Vector2(-50, 0));
        }
        
    }

    void LinearMotion(List<GameObject> target,Vector2 targetPos)
    {
        // 使用 DOTween 插件实现按钮的移动动画
        foreach (var obj in target)
        {
            LinearMotion(obj, targetPos);
        }
    }
    void LinearMotion(GameObject target,Vector2 targetPos)
    {
        // 使用 DOTween 插件实现按钮的移动动画
        if (motionTweens.TryGetValue(target, out var existingTween))
        {
            existingTween.Kill();
        }
     
            motionTweens[target] = target.GetComponent<RectTransform>().DOAnchorPos(targetPos, motionDuration)
                .SetEase(Ease.OutBack);
        
    }
    void OnDisable()
    {
        foreach (var tween in motionTweens.Values)
        {
            tween.Kill();
        }
        motionTweens.Clear();
    }
}
