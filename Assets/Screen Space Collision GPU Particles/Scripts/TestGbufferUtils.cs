using UnityEngine;
using System.Collections;

public class TestGbufferUtils : MonoBehaviour
{
	void Update()
    {
        GetComponent<Renderer>().material.mainTexture = GBufferUtils.GetGBufferTexture(2);
	}
}
