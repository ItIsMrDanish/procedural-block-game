using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ChunkLoadAnimation : MonoBehaviour {

    float speed = 3f;
    Vector3 targetPos;

    float waitTimer;
    float timer;

    private void Start() {

        waitTimer = Random.Range(0f, 3f);
        targetPos = transform.position;

        // Drop chunk below the world to animate it rising up.
        // Previously used ChunkHeight (128), now uses ChunkSize (16) since chunks are cubic.
        transform.position = new Vector3(transform.position.x, -VoxelData.ChunkSize, transform.position.z);
    }

    private void Update() {

        if (timer < waitTimer) {

            timer += Time.deltaTime;
        } else {

            transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * speed);

            if ((targetPos.y - transform.position.y) < 0.05f) {
                transform.position = targetPos;
                Destroy(this);
            }
        }
    }
}