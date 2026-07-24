using UnityEngine;

/// <summary>
/// 动画重定向辅助工具
/// 用于将 Generic 动画应用到不同骨骼命名的角色上
/// </summary>
public class AnimationRetargeter : MonoBehaviour
{
    [Header("源动画")]
    [Tooltip("要播放的动画片段")]
    public AnimationClip sourceClip;

    [Header("目标角色")]
    [Tooltip("目标角色的 Animator")]
    public Animator targetAnimator;

    [Header("骨骼映射")]
    [Tooltip("是否自动尝试映射骨骼")]
    public bool autoMapBones = true;

    [Header("调试")]
    public bool showDebugInfo = true;

    private Animation animationComponent;

    void Start()
    {
        if (sourceClip != null && targetAnimator != null)
        {
            SetupAnimation();
        }
    }

    void SetupAnimation()
    {
        // 添加 Animation 组件用于播放 Generic 动画
        animationComponent = gameObject.AddComponent<Animation>();

        // 添加动画片段
        animationComponent.AddClip(sourceClip, sourceClip.name);

        if (showDebugInfo)
        {
            Debug.Log($"已设置动画: {sourceClip.name}");
            Debug.Log($"动画时长: {sourceClip.length} 秒");
            Debug.Log($"提示: 由于骨骼命名不匹配，此动画可能无法正确显示");
            Debug.Log($"建议: 使用 Humanoid Avatar 重定向或手动调整骨骼映射");
        }
    }

    /// <summary>
    /// 播放动画
    /// </summary>
    public void PlayAnimation()
    {
        if (animationComponent != null && sourceClip != null)
        {
            animationComponent.Play(sourceClip.name);
        }
    }

    /// <summary>
    /// 停止动画
    /// </summary>
    public void StopAnimation()
    {
        if (animationComponent != null)
        {
            animationComponent.Stop();
        }
    }
}
