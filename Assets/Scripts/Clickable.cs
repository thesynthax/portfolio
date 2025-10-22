using UnityEngine;

public class Clickable : MonoBehaviour
{
    public GameObject[] clickable;
    public ContentPopout contentPopout;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if ((contentPopout && contentPopout.isPopped) || (!contentPopout)) {
            if (Input.GetMouseButtonDown(0)) 
            {
                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit, 100f))
                {
                    foreach (var b in clickable)
                    {
                        if (b == null) continue;
                        // check if the hit collider is on same object or under button root hierarchy
                        if (hit.collider != null && hit.collider.transform.IsChildOf(b.transform))
                        {
                            switch (b.name.ToString()) {
                                case ("gsoc"): 
                                    Application.OpenURL("https://wiki.freebsd.org/SummerOfCode2025Projects/MacDoAndMDoImprovements");
                                    break;
                                case ("aq"):
                                    break;
                                case ("tooljet"): 
                                    break;
                                case ("raytracer"):
                                    Application.OpenURL("https://github.com/thesynthax/raytracer");
                                    break;
                                case ("syscalls-viz"): 
                                    Application.OpenURL("https://github.com/thesynthax/syscalls-visualizer");
                                    break;
                                case ("dropifi"): 
                                    Application.OpenURL("https://github.com/thesynthax/dropifi");
                                    break;
                                case ("portfolio"): 
                                    Application.OpenURL("https://github.com/thesynthax/portfolio");
                                    break;
                                case ("andromeda"):
                                    Application.OpenURL("https://www.youtube.com/watch?v=yP3d6HnCZlk");
                                    break;
                                case ("ayigiri"): 
                                    Application.OpenURL("https://www.youtube.com/watch?v=BBfyHWY2H7Q");
                                    break;
                                case ("pv1"): 
                                    Application.OpenURL("https://www.youtube.com/watch?v=O9KEoWfYYn0");
                                    break;
                                case ("yourcall"): 
                                    Application.OpenURL("https://www.youtube.com/watch?v=SpgIHLfB-ZA");
                                    break;
                            }
                        }
                    }
                }
            }
        }
    }
}
