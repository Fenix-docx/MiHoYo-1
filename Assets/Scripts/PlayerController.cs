using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float rotationSpeed = 10f;
    [SerializeField] private float jumpForce = 5f;
    [SerializeField] private float gravity = -15f;
    private bool isGrounded = false;
    public bool isJumping = false;

    private Vector3 moveDirection;
    private Vector3 velocity;
    
    private CharacterController characterController;
    private AnimationController animationController;    

    private void Start()
    {
        characterController = GetComponent<CharacterController>();
        if (characterController == null)
        {
            characterController = gameObject.AddComponent<CharacterController>();
            characterController.center = new Vector3(0, 1, 0);
            characterController.radius = 0.3f;
            characterController.height = 1.6f;
        }

        animationController = GetComponent<AnimationController>();
    }
    void Update()
    {
        if (InputManager.Instance == null || !InputManager.Instance.inputEnable) 
            return;
        Move();//移动

    }
        void FixedUpdate()
        {
            // 应用重力（累积垂直速度）
            velocity.y += gravity * Time.fixedDeltaTime;

            // 应用垂直移动（重力）
            characterController.Move(velocity * Time.fixedDeltaTime);
            
        }

    void Move()
    {
        // 检查是否落地
            isGrounded = characterController.isGrounded;
            //跳跃

            if(isGrounded&&InputManager.Instance.GetJumpPressed())
            {
                isJumping = true;
                velocity.y = jumpForce;
            }
            else
            {
                isJumping = false;
            }

        // 如果在地面上且垂直速度为负，重置垂直速度
        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;  // 小负值确保稳定站在地面
        }
        Vector3 input = InputManager.Instance.GetMoveDir();
            
            if (input.magnitude < 0.1f) 
                return;

            // 获取相机的前后方向，只保留水平分量
            Transform cam = Camera.main.transform;
            Vector3 camForward = Vector3.Scale(cam.forward, new Vector3(1, 0, 1)).normalized;
            Vector3 camRight = Vector3.Scale(cam.right, new Vector3(1, 0, 1)).normalized;

            // 计算世界空间的移动方向（相对于相机）
            moveDirection = (camForward * input.z + camRight * input.x).normalized;

            // 模拟原神控制器
            if (input.z > 0.1f)
            {
                // 使用摄像机的forward方向作为目标方向
                float targetAngle = Mathf.Atan2(camForward.x, camForward.z) * Mathf.Rad2Deg;
                Quaternion targetRotation = Quaternion.Euler(0, targetAngle, 0);

                // 平滑旋转
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, 
                    targetRotation, 
                    Time.fixedDeltaTime * rotationSpeed
                );
            }
            else
            {
                // 其他方向输入时，朝向移动方向
                float targetAngle = Mathf.Atan2(moveDirection.x, moveDirection.z) * Mathf.Rad2Deg;
                Quaternion targetRotation = Quaternion.Euler(0, targetAngle, 0);

                // 平滑旋转
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, 
                    targetRotation, 
                    Time.fixedDeltaTime * rotationSpeed
                );
            }

            // 移动
            characterController.Move(moveDirection * moveSpeed * Time.fixedDeltaTime);

            
    }
    
}       
