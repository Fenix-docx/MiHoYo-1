using UnityEngine;
using System;
public class InputManager : MonoBehaviour
{
    public static InputManager Instance { get; private set; }

    public PlayerInputState inputState{get;private set;}
    [Header("按键设置")]
    public KeyCode jumpKey = KeyCode.Space;
    public KeyCode interactKey = KeyCode.E;
    public float Horizontal { get; private set; }
    public float Vertical { get; private set; }
    public float ScrollWheel { get; private set; }
    public float MouseX { get; private set; }
    public float MouseY { get; private set; }
    public bool JumpPressedThisFrame { get; private set; }
    public bool InteractPressedThisFrame { get; private set; }
    public Vector3 MousePosition { get; private set; }
    [Header("禁用输入")]
    public bool inputEnable = false;
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
    private void Update()
    {   
        if(!inputEnable) return;
        inputState = new PlayerInputState
        {
            moveDir = new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical")),
            forwardDir = new Vector3(0, 0, Input.GetAxisRaw("Vertical")),
            rightDir = new Vector3(Input.GetAxisRaw("Horizontal"), 0, 0),
            ScrollWheel = Input.GetAxis("Mouse ScrollWheel"),
            MouseX = -Input.GetAxis("Mouse Y"),
            MouseY = Input.GetAxis("Mouse X"),
            jumpPressed = Input.GetKeyDown(jumpKey),
            interactPressed = Input.GetKeyDown(interactKey),
            lookDir = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")),
            CancelPressed = Input.GetKeyDown(KeyCode.Escape)
        };

    }
    //外部调用
    public void EnableInput( bool enable )=> inputEnable = enable;
    public Vector3 GetMoveDir()=> inputState.moveDir;
    public Vector3 GetForwardDir()=> inputState.forwardDir;
    public Vector3 GetRightDir()=> inputState.rightDir;
    public bool GetJumpPressed()=> inputState.jumpPressed;
    public bool GetInteractPressed()=> inputState.interactPressed;
    public bool GetCancelPressed()=> inputState.CancelPressed;
    public Vector2 GetLookDir()=> inputState.lookDir;
    public float GetScrollWheel()=> inputState.ScrollWheel;
    public float GetMouseX()=> inputState.MouseX;
    public float GetMouseY()=> inputState.MouseY;
}