using Unity.Microsoft.GDK;
using UnityEngine;

public class AnimationController : MonoBehaviour
{
    [Header("动画状态机参数")]
    public Animator animator;
    public string moveSpeedParam = "MoveSpeed";
    public string isMovingParam = "IsMoving";
    public string isJumpingParam = "IsJumping";
    public PlayerController playerController;

    [Header("行走/奔跑阈值")]
    public float startWalkThreshold = 0.01f;
    public float walkThreshold = 0.8f;
    [Header("跳跃阈值")]
    public bool isJumping => playerController.isJumping;

    private static readonly int MoveSpeedHash = Animator.StringToHash("MoveSpeed");
    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
    void Start()
    {
        if (animator == null)
        animator = GetComponent<Animator>();
        if(playerController == null)
        playerController = GameObject.FindWithTag("Player").GetComponent<PlayerController>();
    }

    private void Update()
    {
        if (InputManager.Instance == null || animator == null) return;

        Vector3 moveDir = InputManager.Instance.GetMoveDir();
        float magnitude = new Vector3(moveDir.x, 0, moveDir.z).magnitude;
        
        animator.SetFloat(MoveSpeedHash, magnitude);
        animator.SetBool(IsMovingHash, magnitude > startWalkThreshold);
        // animator.SetFloat("Forward", magnitude);
        animator.SetFloat("Turn", moveDir.x);

        if (isJumping)
        {
            print("跳跃");
            animator.SetBool(isJumpingParam, true);
        }
        else
        {
            animator.SetBool(isJumpingParam, false);
        }
        
    }
}
