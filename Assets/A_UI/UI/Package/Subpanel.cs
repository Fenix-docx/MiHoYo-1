using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
namespace UIPackage
{
public class Subpanel : UIBase

{
    private Button m_BackButton;
    private Button m_PushButton;
    private Button m_RootButton;

    void Awake()
    {
        m_BackButton = FindButton("BackButton");
        m_PushButton = FindButton("PushButton");
        m_RootButton = FindButton("RootButton");
    }

    private Button FindButton(string buttonName)
    {
        return GetComponentsInChildren<Button>(true)
            .FirstOrDefault(b => b.name == buttonName);
    }

    // Start is called before the first frame update
    void Start()
    {
        

    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
}