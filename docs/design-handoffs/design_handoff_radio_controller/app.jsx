// app.jsx — Radio Controller design improvement canvas
const { useEffect } = React;

function App() {
  useEffect(() => { document.title = 'Radio Controller — Visual Handoff'; }, []);

  return (
    <DesignCanvas>

      <DCSection
        id="composed"
        title="Composed — the tuner page after"
        subtitle="The complete proposed Radio source view, pulling together every change in this package. Shown first so the rest of the canvas has context."
      >
        <DCArtboard id="tuner-composed" label="Tuner page · 1920 × 720" width={1920} height={780}>
          <Mock_TunerComposed />
        </DCArtboard>
      </DCSection>


      <DCSection
        id="p0"
        title="P0 — bugs and confusing readouts"
        subtitle="Things that actively mislead the user. Each of these has been seen wrong on the live device."
      >
        <DCArtboard id="signal" label="Signal meter — 118 % is impossible" width={1280} height={460}>
          <Mock_SignalMeter />
        </DCArtboard>

        <DCArtboard id="agc" label="AGC / gain strip — never half-empty" width={1280} height={520}>
          <Mock_AgcStrip />
        </DCArtboard>

        <DCArtboard id="conf" label="Song recognition — kill the 80 % column" width={1480} height={720}>
          <Mock_SongRecognition />
        </DCArtboard>
      </DCSection>


      <DCSection
        id="p1"
        title="P1 — structural improvements"
        subtitle="The product feels twice as finished after these land."
      >
        <DCArtboard id="header" label="Tuner header & RDS — promote what the user actually needs" width={1280} height={560}>
          <Mock_TunerHeader />
        </DCArtboard>

        <DCArtboard id="presets" label="Memory presets — name, slot, frequency in three columns" width={1100} height={620}>
          <Mock_Presets />
        </DCArtboard>

        <DCArtboard id="status" label="Now Playing status — one strip, not three pills" width={1280} height={620}>
          <Mock_NowPlayingStatus />
        </DCArtboard>
      </DCSection>


      <DCSection
        id="p2"
        title="P2 — polish"
        subtitle="Smaller surfaces that punch above their pixel count."
      >
        <DCArtboard id="gain-pop" label="Gain control popover — peak meter + AUTO + reset" width={1280} height={520}>
          <Mock_GainPopover />
        </DCArtboard>
      </DCSection>

    </DesignCanvas>
  );
}

ReactDOM.createRoot(document.getElementById('root')).render(<App />);
