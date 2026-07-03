using UnityEngine;

[System.Serializable]
public struct PlayerInputState
{
    //移动
    public Vector3 moveDir;
    public Vector3 forwardDir;
    public Vector3 rightDir;

    public bool jumpPressed;
    //视角
    public Vector2 lookDir;
    public float MouseX;
    public float MouseY;
    public float ScrollWheel;

    //玩家交互
    public bool interactPressed;
    //UI控制
    public bool CancelPressed;
}