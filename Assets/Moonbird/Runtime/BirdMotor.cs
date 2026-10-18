using UnityEngine;

namespace Moonbird
{
    public sealed class BirdMotor : MonoBehaviour
    {
        public Rigidbody2D Body { get; private set; }
        public bool Grounded { get; private set; }
        public bool Playing { get; private set; }
        public bool ReadyPose { get; set; }
        public int Jumps { get; private set; }
        public int Falls { get; private set; }
        public bool ReachedMoon { get; private set; }
        public event System.Action Landed;
        public event System.Action Jumped;
        public event System.Action Respawned;
        public event System.Action Finished;

        MapDocument map;
        WorldView world;
        Vector2 origin;
        Vector2? queuedVelocity;
        readonly ContactPoint2D[] contacts = new ContactPoint2D[16];
        float launchGrace, landingPulse;

        public void Initialize(WorldView view) { world = view; Body = view.BirdBody; }

        public void Begin(MapDocument document, Vector2 start)
        {
            map = document.Clone();
            origin = start;
            Playing = true;
            ReachedMoon = false;
            Jumps = Falls = 0;
            Body.simulated = true;
            Respawn();
        }

        public void Stop()
        {
            Playing = false;
            ReadyPose = false;
            queuedVelocity = null;
            Body.linearVelocity = Vector2.zero;
            Body.simulated = false;
            world.BirdVisual.localScale = Vector3.one;
        }

        public void Respawn()
        {
            Body.position = origin;
            Body.linearVelocity = Vector2.zero;
            Body.angularVelocity = 0;
            Grounded = false;
            ReadyPose = false;
            queuedVelocity = null;
            launchGrace = .08f;
            Respawned?.Invoke();
        }

        public bool QueueLaunch(Vector2 velocity)
        {
            if (!Playing || !Grounded || ReachedMoon || queuedVelocity.HasValue) return false;
            queuedVelocity = velocity;
            return true;
        }

        void FixedUpdate()
        {
            if (!Playing || ReachedMoon) return;
            var wasGrounded = Grounded;
            Grounded = false;
            if (launchGrace <= 0 && Body.linearVelocity.y <= .15f)
            {
                var count = Body.GetContacts(contacts);
                for (var i = 0; i < count; i++)
                    if (contacts[i].normal.y > .65f && contacts[i].collider != world.GoalCollider)
                    { Grounded = true; break; }
            }
            launchGrace -= Time.fixedDeltaTime;
            if (Grounded)
            {
                Body.linearVelocity = new Vector2(0, Body.linearVelocity.y);
                if (!wasGrounded) { landingPulse = 1; Landed?.Invoke(); }
            }
            if (queuedVelocity.HasValue)
            {
                if (Grounded)
                {
                    Body.linearVelocity = queuedVelocity.Value;
                    Grounded = false;
                    launchGrace = .08f;
                    Jumps++;
                    Jumped?.Invoke();
                }
                queuedVelocity = null;
            }
            if (Body.position.y < map.killY) { Falls++; Respawn(); }
            if (Vector2.Distance(Body.position, map.goal) < 1.15f)
            {
                ReachedMoon = true;
                Body.linearVelocity = Vector2.zero;
                Body.simulated = false;
                Finished?.Invoke();
            }
        }

        void Update()
        {
            if (!Playing) return;
            landingPulse = Mathf.MoveTowards(landingPulse, 0, Time.deltaTime * 5);
            var squash = ReadyPose && Grounded ? .1f : landingPulse * .17f;
            world.BirdVisual.localScale = new Vector3(1 + squash, 1 - squash, 1);
        }
    }
}
