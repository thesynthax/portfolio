using UnityEngine;
using UnityEngine.UI;

public class SocialButtons : MonoBehaviour
{
    public Button[] socialButtons;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetMouseButtonDown(0)) 
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 100f))
            {
                foreach (var b in socialButtons)
                {
                    if (b == null) continue;
                    // check if the hit collider is on same object or under button root hierarchy
                    if (hit.collider != null && hit.collider.transform.IsChildOf(b.transform))
                    {
                        switch (b.name.ToString()) {
                            case ("linkedin"): 
                                Application.OpenURL("https://linkedin.com/in/thesynthax");
                                break;
                            case ("github"):
                                Application.OpenURL("https://github.com/thesynthax");
                                break;
                            case ("x"): 
                                Application.OpenURL("https://x.com/thesynthaxx");
                                break;
                            case ("youtube"):
                                Application.OpenURL("https://youtube.com/@TheSynthax");
                                break;
                            case ("hashnode"): 
                                Application.OpenURL("https://thesynthax.hashnode.dev");
                                break;
                        }
                    }
                }
            }
        }
    }
}
