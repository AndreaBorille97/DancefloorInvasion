using UnityEngine;

/// Aggiunto automaticamente agli oggetti inghiottiti: li stira, li risucchia e li rimuove.
public class BlackHoleConsumable : MonoBehaviour
{
    public bool IsBeingConsumed { get; private set; }

    BlackHole _hole;
    float _t;
    Vector3 _startScale, _startOffset;

    public void Begin(BlackHole hole)
    {
        if (IsBeingConsumed) return;
        IsBeingConsumed = true;
        _hole = hole;
        _t = 0f;
        _startScale = transform.localScale;
        _startOffset = transform.position - hole.transform.position;

        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
        if (TryGetComponent(out Rigidbody rb))
        {
            BlackHoleUtil.SetVelocity(rb, Vector3.zero);
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }
    }

    void Update()
    {
        if (!IsBeingConsumed) return;
        if (_hole == null) { Destroy(gameObject); return; }

        _t += Time.deltaTime / _hole.consumeDuration;
        float k = Mathf.Clamp01(_t);
        float e = k * k;
        Vector3 c = _hole.transform.position;

        Vector3 off = Quaternion.AngleAxis(180f * _hole.diskSpin * k, _hole.transform.up) * _startOffset;
        transform.position = c + off * (1f - e);

        if (_hole.spaghettify)
        {
            Vector3 toC = c - transform.position;
            if (toC.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(toC);
            float st = 1f + _hole.stretchStrength * k;
            float thin = (1f - e) / Mathf.Sqrt(st);
            transform.localScale = new Vector3(_startScale.x * thin, _startScale.y * thin, _startScale.z * st * (1f - e));
        }
        else
        {
            transform.localScale = _startScale * (1f - e);
        }

        if (k >= 1f)
        {
            if (_hole.destroyConsumed) Destroy(gameObject);
            else gameObject.SetActive(false);
        }
    }
}
