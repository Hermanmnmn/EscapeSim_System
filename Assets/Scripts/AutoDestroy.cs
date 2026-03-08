using UnityEngine;

public class AutoDestroy : MonoBehaviour
{
    public float lifeTime = 10f;

    void Start()
    {
        Destroy(gameObject, lifeTime);
    }
}
