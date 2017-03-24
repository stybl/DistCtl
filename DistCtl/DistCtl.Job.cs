﻿namespace DistCtl
{
    internal sealed class Job
    {
        private readonly DistCommon.Job.Blueprint blueprint;

        public Job(DistCommon.Job.Blueprint blueprint, int nodeID, bool awake)
        {
            this.blueprint = blueprint;
            this.NodeID = nodeID;
            this.Awake = awake;
        }

        public int NodeID { get; private set; }

        public bool Awake { get; private set; }

        public DistCommon.Job.Blueprint Blueprint
        {
            get
            {
                return this.blueprint;
            }
        }

        public void Transfer(int id)
        {
            this.NodeID = id;
        }

        public void Sleep()
        {
            this.Awake = false;
        }

        public void Wake()
        {
            this.Awake = true;
        }
    }
}
