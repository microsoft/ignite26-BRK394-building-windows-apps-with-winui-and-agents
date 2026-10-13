'use strict';
// Port of Hangar.Services.FleetDataService — deterministic seed (2207) identical to the WinUI app.

class DotNetRandom {
  // .NET legacy Knuth subtractive generator (Net5CompatSeedImpl) so the data matches exactly.
  constructor(seed) {
    const MBIG = 2147483647, MSEED = 161803398;
    const sa = new Array(56).fill(0);
    let subtraction = seed === -2147483648 ? MBIG : Math.abs(seed);
    let mj = MSEED - subtraction;
    sa[55] = mj;
    let mk = 1, ii = 0;
    for (let i = 1; i < 55; i++) {
      if ((ii += 21) >= 55) ii -= 55;
      sa[ii] = mk;
      mk = mj - mk;
      if (mk < 0) mk += MBIG;
      mj = sa[ii];
    }
    for (let k = 1; k < 5; k++) {
      for (let i = 1; i < 56; i++) {
        let n = i + 30;
        if (n >= 55) n -= 55;
        sa[i] -= sa[1 + n];
        if (sa[i] < 0) sa[i] += MBIG;
      }
    }
    this.sa = sa; this.inext = 0; this.inextp = 21; this.MBIG = MBIG;
  }
  internalSample() {
    let a = this.inext + 1; if (a >= 56) a = 1;
    let b = this.inextp + 1; if (b >= 56) b = 1;
    let r = this.sa[a] - this.sa[b];
    if (r === this.MBIG) r--;
    if (r < 0) r += this.MBIG;
    this.sa[a] = r; this.inext = a; this.inextp = b;
    return r;
  }
  sample() { return this.internalSample() * (1.0 / this.MBIG); }
  next(a, b) { return b === undefined ? Math.trunc(this.sample() * a) : Math.trunc(this.sample() * (b - a)) + a; }
  nextDouble() { return this.sample(); }
}

// Banker's rounding (Math.Round default MidpointRounding.ToEven).
function roundEven(v, digits) {
  const m = Math.pow(10, digits);
  const x = v * m;
  const f = Math.floor(x);
  const diff = x - f;
  let r;
  if (Math.abs(diff - 0.5) < 1e-9) r = f % 2 === 0 ? f : f + 1;
  else r = Math.round(x);
  return r / m;
}

const HeroTail = 'N2207C';
const HeroFlight = 'CA 2207';
// DateTime arithmetic is done in UTC so DST never shifts values (mirrors .NET DateTime, Kind=Unspecified).
const Now = Date.UTC(2026, 10, 18, 2, 40);
const Departure = Date.UTC(2026, 10, 18, 6, 15);
const VibrationThreshold = 1.8;
const AvailabilityTarget = 98;
const DAY = 86400000, MIN = 60000;

const Components = ['Fan blade', 'HPT blade', 'Compressor', 'Nacelle', 'Landing gear', 'Hydraulics', 'Avionics', 'Brakes', 'APU', 'Cabin', 'Fuel system', 'Flight controls'];
const Technicians = ['Maya Okafor', 'Diego Ramos', 'Priya Natarajan', 'Tom Becker', 'Aiko Tanaka', 'Samuel Mensah', 'Elena Petrova', 'Jonas Lindqvist', 'Fatima Haddad', 'Chris Walker', 'Lucía Fernández', 'Wei Chen'];
const Types = ['A321neo', 'A320neo', 'A321neo', '737 MAX 8', 'A220-300'];
const Stations = ['SEA', 'SFO', 'DEN', 'ORD', 'JFK', 'LAX', 'ATL', 'BOS'];
const AtaByComponent = {
  'Fan blade': '72-21', 'HPT blade': '72-51', 'Compressor': '72-31', 'Nacelle': '71-11', 'Landing gear': '32-11', 'Hydraulics': '29-11',
  'Avionics': '34-41', 'Brakes': '32-42', 'APU': '49-10', 'Cabin': '25-21', 'Fuel system': '28-21', 'Flight controls': '27-51',
};
const FindingsByComponent = {
  'Fan blade': ['Leading-edge nick, within limits', 'Erosion on blade tip', 'FOD strike, blend repair', 'Vibration trend review'],
  'HPT blade': ['Coating loss stage 1', 'Tip curl, monitor', 'Thermal distress noted'],
  'Compressor': ['Wash performed', 'Stage 3 blade dent', 'Bleed valve sticking'],
  'Nacelle': ['Fan cowl latch worn', 'Thrust reverser seal', 'Acoustic panel delam'],
  'Landing gear': ['Shock strut servicing', 'Tire wear at limit', 'Downlock actuator leak'],
  'Hydraulics': ['Green system pressure low', 'Line chafing', 'Reservoir overfill'],
  'Avionics': ['ADIRU fault msg', 'Weather radar intermittent', 'FMS database load'],
  'Brakes': ['Wear pin at limit', 'Brake temp high #3', 'Accumulator precharge'],
  'APU': ['Oil consumption high', 'Start fault', 'Inlet door actuator'],
  'Cabin': ['Seat 14C recline', 'Lav smoke detector', 'PSU light inop'],
  'Fuel system': ['Quantity indication drift', 'Boost pump pressure low', 'Vent valve'],
  'Flight controls': ['Spoiler 3 servo', 'Flap asymmetry msg', 'Trim actuator play'],
};
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const pad2 = n => String(n).padStart(2, '0');

function mapPart(c) {
  return c === 'Fan blade' ? 'Fan' : c === 'Compressor' ? 'Compressor' : c === 'HPT blade' ? 'Turbine' : c === 'Nacelle' ? 'Nacelle' : 'None';
}
function addMonths(t, m) {
  const d = new Date(t);
  const y = d.getUTCFullYear(), mo = d.getUTCMonth() + m;
  const ny = y + Math.floor(mo / 12), nm = ((mo % 12) + 12) % 12;
  const dim = new Date(Date.UTC(ny, nm + 1, 0)).getUTCDate();
  return Date.UTC(ny, nm, Math.min(d.getUTCDate(), dim), d.getUTCHours(), d.getUTCMinutes());
}
function fmtDate(t) { const d = new Date(t); return `${pad2(d.getUTCDate())} ${MONTHS[d.getUTCMonth()]} ${d.getUTCFullYear()}`; }
function fmtTime(t) { const d = new Date(t); return `${pad2(d.getUTCHours())}:${pad2(d.getUTCMinutes())}`; }
const StatusText = { Open: 'Open', InProgress: 'In progress', Deferred: 'Deferred', Closed: 'Closed' };
const StatusOrder = ['Open', 'InProgress', 'Deferred', 'Closed'];

const WoProto = {
  get numberText() { return 'WO-' + this.number; },
  get openedText() { return fmtDate(this.opened); },
  get statusText() { return StatusText[this.status]; },
  get isMappable() { return this.mappedPart !== 'None'; },
};
function makeWo(o) {
  o.signedBy = o.signedBy || null;
  return Object.setPrototypeOf(o, WoProto);
}

class FleetDataService {
  constructor() {
    this.listeners = { heroStatusChanged: [], reset: [] };
    this.seed();
  }
  on(evt, fn) { this.listeners[evt].push(fn); return () => { this.listeners[evt] = this.listeners[evt].filter(f => f !== fn); }; }
  emit(evt) { for (const f of this.listeners[evt].slice()) f(); }

  seed() {
    const rng = new DotNetRandom(2207);
    this.seedFleet(rng);
    this.seedWorkOrders(rng);
    this.seedTelemetry(rng);
  }

  get hero() { return this.fleet.find(a => a.tail === HeroTail); }
  get todayAvailability() { return this.hero.status === 'Healthy' ? 98.1 : 97.6; }
  get openWorkOrderCount() { return 128 - (this.hero.status === 'Healthy' ? 1 : 0); }

  seedFleet(rng) {
    const fleet = [];
    const used = new Set([HeroTail]);
    for (let i = 0; i < 42; i++) {
      let tail, flight, type;
      if (i === 2) { tail = HeroTail; flight = HeroFlight; type = 'A321neo'; }
      else {
        do {
          const n = rng.next(100, 999);
          const a = String.fromCharCode(65 + rng.next(0, 26));
          const b = String.fromCharCode(65 + rng.next(0, 26));
          tail = `N${n}${a}${b}`;
        } while (used.has(tail));
        used.add(tail);
        flight = `CA ${rng.next(100, 2999)}`;
        type = Types[rng.next(Types.length)];
      }
      let status = 'Healthy', note = 'Ready';
      const hero = tail === HeroTail;
      if (hero) { status = 'Attention'; note = 'Eng 2 vibration exceedance'; }
      else if (i === 23) { status = 'Aog'; note = 'AOG · Hydraulic pump'; }
      else if ([3, 15, 31, 38].includes(i)) { status = 'Maintenance'; note = 'Scheduled A-check'; }
      const station = hero ? 'SEA' : Stations[rng.next(Stations.length)];
      let nextDeparture;
      if (hero) nextDeparture = '06:15';
      else { const h = rng.next(5, 12); const m = rng.next(0, 12) * 5; nextDeparture = `${pad2(h)}:${pad2(m)}`; }
      const cycles = hero ? 8412 : rng.next(1200, 14000);
      const flightHours = hero ? 19877 : rng.next(3000, 32000);
      const daysToNextCheck = hero ? 23 : rng.next(2, 90);
      fleet.push({ tail, flight, type, station, nextDeparture, cycles, flightHours, daysToNextCheck, status, statusNote: note });
    }
    this.fleet = fleet;
  }

  seedWorkOrders(rng) {
    const list = [];
    let number = 48100;
    for (let i = 0; i < 2398; i++) {
      const component = Components[rng.next(Components.length)];
      const findings = FindingsByComponent[component];
      const aircraft = this.fleet[rng.next(this.fleet.length)];
      const days = rng.next(0, 730);
      const mins = rng.next(0, 1440);
      const opened = Now - days * DAY - mins * MIN;
      const ageDays = (Now - opened) / DAY;
      const status = ageDays > 21
        ? (rng.nextDouble() < 0.94 ? 'Closed' : 'Deferred')
        : StatusOrder[rng.next(0, 4)];
      const pn = `${rng.next(1000, 9999)}M${rng.next(10, 99)}P${pad2(rng.next(1, 20))}`;
      const technician = Technicians[rng.next(Technicians.length)];
      const finding = findings[rng.next(findings.length)];
      const hours = roundEven(0.5 + rng.nextDouble() * 11.5, 1);
      list.push(makeWo({
        number: number++, tail: aircraft.tail, component, ata: AtaByComponent[component], partNumber: pn,
        technician, opened, mappedPart: mapPart(component), finding, status, hours,
      }));
    }
    list.push(makeWo({
      number: 46871, tail: HeroTail, component: 'Fan blade', ata: AtaByComponent['Fan blade'], partNumber: '2307M41P04',
      technician: 'Diego Ramos', opened: addMonths(Now, -11) - 3 * DAY, mappedPart: 'Fan',
      finding: 'Eng 2 N1 vibration · blade 14 nick, blended', status: 'Closed', hours: 6.5,
    }));
    list.push(makeWo({
      number: 50498, tail: HeroTail, component: 'Fan blade', ata: AtaByComponent['Fan blade'], partNumber: '2307M41P04',
      technician: 'Maya Okafor', opened: Now - 26 * MIN, mappedPart: 'Fan',
      finding: 'Eng 2 N1 vibration exceedance · borescope', status: 'InProgress', hours: 1.0,
    }));
    list.sort((a, b) => b.opened - a.opened);
    this.workOrders = list;
  }

  seedTelemetry(rng) {
    const vib = [], avg = [];
    for (let i = 0; i < 50; i++) {
      avg.push(0.95 + Math.sin(i / 7) * 0.05);
      vib.push(roundEven(0.9 + rng.nextDouble() * 0.35 + Math.sin(i / 5) * 0.08, 2));
    }
    vib[46] = 1.42; vib[47] = 1.61; vib[48] = 1.74; vib[49] = 2.31;
    this.vibration = vib; this.vibrationAverage = avg;
    const av = [];
    for (let i = 0; i < 30; i++) av.push(roundEven(98.1 + Math.sin(i / 3.2) * 0.5 + rng.nextDouble() * 0.5, 2));
    av[28] = 98.3; av[29] = this.todayAvailability;
    this.availability = av;
    const spark = (c, a, last) => {
      const s = [];
      for (let i = 0; i < 16; i++) s.push(roundEven(c + (rng.nextDouble() - 0.5) * 2 * a, 2));
      s[15] = last; return s;
    };
    this.availSpark = spark(98, 0.6, 98); this.availSpark[15] = this.todayAvailability;
    this.aogSpark = [2, 1, 1, 0, 0, 1, 2, 1, 0, 0, 1, 1, 0, 0, 1, 1];
    this.openWoSpark = spark(135, 8, 128);
    this.onTimeSpark = spark(92, 2.5, 94);
  }

  signOff(technician, note) {
    let wo = this.workOrders.find(w => w.tail === HeroTail && w.component === 'Fan blade' && w.status !== 'Closed');
    if (!wo) {
      const max = Math.max(...this.workOrders.map(w => w.number));
      wo = makeWo({
        number: max + 1, tail: HeroTail, component: 'Fan blade', ata: AtaByComponent['Fan blade'], partNumber: '2307M41P04',
        technician, opened: Now, mappedPart: 'Fan', finding: note, status: 'Open', hours: 1.5,
      });
      this.workOrders.unshift(wo);
    }
    wo.finding = note; wo.status = 'Closed'; wo.signedBy = technician;
    const h = this.hero; h.status = 'Healthy'; h.statusNote = 'Released to service · 03:12';
    this.availability[this.availability.length - 1] = this.todayAvailability;
    this.availSpark[this.availSpark.length - 1] = this.todayAvailability;
    this.emit('heroStatusChanged');
  }

  resetDemo() { this.seed(); this.emit('reset'); }
}

window.Hangar = window.Hangar || {};
Object.assign(window.Hangar, {
  DotNetRandom, FleetDataService, HeroTail, HeroFlight, Now, Departure, VibrationThreshold, AvailabilityTarget,
  fmtDate, fmtTime, pad2, StatusText,
  data: new FleetDataService(),
});
