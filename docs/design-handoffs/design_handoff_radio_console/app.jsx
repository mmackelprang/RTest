// handoff-app.jsx — assemble the design canvas

const { useEffect } = React;

function App() {
  useEffect(() => { document.title = 'Radio Console — Visual Handoff'; }, []);

  return (
    <DesignCanvas>

      <DCSection
        id="p0"
        title="P0 — fix before anything else"
        subtitle="Bugs and IDs leaking through the surface. Each of these makes the product look unfinished or untrustworthy."
      >
        <DCArtboard id="topbar" label="Top bar redesign" width={1920} height={420}>
          <Mock_TopBar />
        </DCArtboard>

        <DCArtboard id="naming" label="Source &amp; device names" width={880} height={460}>
          <Mock_Naming />
        </DCArtboard>

        <DCArtboard id="queue" label="Queue rows &amp; duration format" width={1280} height={540}>
          <Mock_Queue />
        </DCArtboard>

        <DCArtboard id="metrics" label="Metrics tiles — units, groups, trends" width={1280} height={580}>
          <Mock_Metrics />
        </DCArtboard>

        <DCArtboard id="fbfilter" label="File browser filter — chips" width={880} height={360}>
          <Mock_FbFilter />
        </DCArtboard>
      </DCSection>


      <DCSection
        id="p1"
        title="P1 — big quality wins"
        subtitle="Behavioural / structural fixes. The product feels twice as finished after these land."
      >
        <DCArtboard id="dock" label="Persistent Now Playing dock" width={1920} height={760}>
          <Mock_Dock />
        </DCArtboard>

        <DCArtboard id="pill" label="Source pill semantics — one action + chevron" width={1100} height={320}>
          <Mock_PillSemantics />
        </DCArtboard>

        <DCArtboard id="viz" label="Visualizer panel — promoted mode picker" width={1480} height={620}>
          <Mock_Visualizer />
        </DCArtboard>

        <DCArtboard id="qsplit" label="Queue page — split layout" width={1200} height={660}>
          <Mock_QueueSplit />
        </DCArtboard>

        <DCArtboard id="skel" label="Skeleton loading states" width={1280} height={660}>
          <Mock_Skeleton />
        </DCArtboard>
      </DCSection>


      <DCSection
        id="system"
        title="System polish"
        subtitle="Things that change how the product feels in the room, not just how it looks on a screenshot."
      >
        <DCArtboard id="sleep" label="Sleep — ambient screen" width={1920} height={760}>
          <Mock_Sleep />
        </DCArtboard>

        <DCArtboard id="dev" label="Dev tools — gesture-gated tray" width={880} height={620}>
          <Mock_DevTools />
        </DCArtboard>
      </DCSection>

    </DesignCanvas>
  );
}

ReactDOM.createRoot(document.getElementById('root')).render(<App />);
