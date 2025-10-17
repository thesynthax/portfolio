using UnityEngine;
using UnityEngine.UI;

public class Test : MonoBehaviour
{
    public float speed = 5f;
    public float phase = 90f;
    public Slider speedSlider;
    public InputField phaseText;
    Vector3 initPosition;
    
    private void Start()
    {
        initPosition = transform.position;
    }

    void Update()
    {
        this.speed = speedSlider.value;
        this.phase = float.Parse(phaseText.text);
        transform.position = new Vector3(initPosition.x + Mathf.Sin(Time.time * speed + Mathf.Deg2Rad * phase), initPosition.y + Mathf.Sin(Time.time * speed), initPosition.z);
    }
}
