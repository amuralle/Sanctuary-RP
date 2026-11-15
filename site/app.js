const PAGE_SIZE = 80;

const collectionMaps = [
  {
    key: "briarwood",
    zoneId: 1,
    name: "Briarwood",
    imageUrl: "assets/maps/briarwood.webp",
    pools: ["briarwood-mushrooms", "briarwood-mushrooms-rare"],
    minX: -1024,
    maxX: 0,
    minZ: 1536,
    maxZ: 2560,
  },
  {
    key: "blackspore",
    zoneId: 1,
    name: "Blackspore",
    imageUrl: "assets/maps/blackspore.webp",
    pools: ["blackspore-fossils", "blackspore-fossils-rare"],
    minX: -2688,
    maxX: -1664,
    minZ: 768,
    maxZ: 1664,
  },
  {
    key: "stillwater",
    zoneId: 1,
    name: "Stillwater Crossing",
    imageUrl: "assets/maps/stillwater.webp",
    pools: [
      "stillwater-sparkling-water",
      "stillwater-sparkling-water-rare",
      "stillwater-flowers",
      "stillwater-flowers-rare",
      "stillwater-robgoblin-junk",
      "stillwater-robgoblin-junk-rare",
    ],
    minX: -1792,
    maxX: -768,
    minZ: 512,
    maxZ: 1536,
  },
  {
    key: "nettleseed",
    zoneId: 1,
    name: "Nettleseed",
    imageUrl: "assets/maps/central-wilds.webp",
    pools: [
      "nettleseed-sparkling-water",
      "nettleseed-sparkling-water-rare",
    ],
    minX: -512,
    maxX: 1408,
    minZ: 0,
    maxZ: 1920,
    autoFitPlacements: true,
  },
  {
    key: "merryvale",
    zoneId: 1,
    name: "Merry Vale",
    imageUrl: "assets/maps/sanctuary.webp",
    pools: [
      "merryvale-sparkling-water",
      "merryvale-sparkling-water-rare",
    ],
    minX: -512,
    maxX: 1408,
    minZ: 0,
    maxZ: 1920,
    autoFitPlacements: true,
  },
  {
    key: "cobblestone",
    zoneId: 1,
    name: "Cobblestone Village",
    imageUrl: "assets/maps/cobblestone-village.webp",
    pools: [
      "southern-wilds-flowers",
      "southern-wilds-flowers-rare",
      "southern-wilds-leaves",
      "southern-wilds-leaves-rare",
      "southern-wilds-robgoblin-junk",
      "southern-wilds-robgoblin-junk-rare",
    ],
    minX: -2304,
    maxX: -1536,
    minZ: 0,
    maxZ: 768,
  },
  {
    key: "lakeshore",
    zoneId: 1,
    name: "Lakeshore",
    imageUrl: "assets/maps/lakeshore.webp",
    pools: [
      "lakeshore-sparkling-water",
      "lakeshore-sparkling-water-rare",
      "lakeshore-flowers",
      "lakeshore-flowers-rare",
    ],
    minX: -768,
    maxX: 256,
    minZ: -256,
    maxZ: 512,
    autoFitPlacements: true,
  },
  {
    key: "seaside",
    zoneId: 1,
    name: "Seaside",
    imageUrl: "assets/maps/seaside.webp",
    pools: [
      "seaside-shells",
      "seaside-shells-rare",
      "seaside-sparkling-water",
      "seaside-sparkling-water-rare",
    ],
    minX: 512,
    maxX: 2048,
    minZ: -512,
    maxZ: 768,
    autoFitPlacements: true,
  },
  {
    key: "snowhill",
    zoneId: 1,
    name: "Snowhill",
    imageUrl: "assets/maps/snowhill.webp",
    pools: [
      "snowhill-flowers",
      "snowhill-flowers-rare",
      "snowhill-leaves",
      "snowhill-leaves-rare",
      "snowhill-sparkling-water",
      "snowhill-sparkling-water-rare",
    ],
    minX: -256,
    maxX: 896,
    minZ: -1536,
    maxZ: -512,
    autoFitPlacements: true,
  },
  {
    key: "sanctuary-central",
    zoneId: 1,
    name: "Sanctuary / Central Wilds",
    imageUrl: "assets/maps/sanctuary-central.webp",
    pools: [
      "sanctuary-central-sparkling-water",
      "sanctuary-central-sparkling-water-rare",
      "sanctuary-central-flowers",
      "sanctuary-central-flowers-rare",
      "sanctuary-central-robgoblin-junk",
      "sanctuary-central-robgoblin-junk-rare",
    ],
    minX: -512,
    maxX: 1408,
    minZ: 0,
    maxZ: 1920,
    autoFitPlacements: true,
  },
  {
    key: "southern-wilds",
    zoneId: 1,
    name: "Southern Wilds",
    imageUrl: "assets/maps/southern-wilds.webp",
    pools: [
      "southern-wilds-flowers",
      "southern-wilds-flowers-rare",
      "southern-wilds-leaves",
      "southern-wilds-leaves-rare",
      "southern-wilds-robgoblin-junk",
      "southern-wilds-robgoblin-junk-rare",
    ],
    minX: -2560,
    maxX: -1024,
    minZ: -512,
    maxZ: 1536,
    autoFitPlacements: true,
  },
];

const collectionPoolKeys = [...new Set(collectionMaps.flatMap((map) => map.pools))].sort();

const state = {
  models: [],
  commands: [],
  catalogImages: new Map(),
  filtered: [],
  visibleCount: PAGE_SIZE,
  selectedId: null,
  user: null,
  placements: [],
  filteredPlacements: [],
  selectedPlacementId: null,
};

const elements = {
  tabs: [...document.querySelectorAll(".tab-button")],
  views: [...document.querySelectorAll("[data-view-panel]")],
  search: document.querySelector("#model-search"),
  category: document.querySelector("#category-filter"),
  fit: document.querySelector("#fit-filter"),
  results: document.querySelector("#model-results"),
  resultCount: document.querySelector("#result-count"),
  loadMore: document.querySelector("#load-more"),
  detailTitle: document.querySelector("#detail-title"),
  detailCategory: document.querySelector("#detail-category"),
  detailContext: document.querySelector("#detail-context"),
  detailMetadata: document.querySelector("#detail-metadata"),
  detailImage: document.querySelector("#detail-image"),
  detailPlaceholder: document.querySelector("#detail-placeholder"),
  imageCredit: document.querySelector("#image-credit"),
  chooseImage: document.querySelector("#choose-image"),
  imageFile: document.querySelector("#image-file"),
  commandBuilder: document.querySelector("#command-builder"),
  spawnName: document.querySelector("#spawn-name"),
  spawnGroup: document.querySelector("#spawn-group"),
  spawnCommand: document.querySelector("#spawn-command"),
  copySpawn: document.querySelector("#copy-spawn"),
  commandList: document.querySelector("#command-list"),
  accountName: document.querySelector("#account-name"),
  accountLogin: document.querySelector("#account-login"),
  accountLogout: document.querySelector("#account-logout"),
  loginDialog: document.querySelector("#login-dialog"),
  loginForm: document.querySelector("#login-form"),
  loginUsername: document.querySelector("#login-username"),
  loginPassword: document.querySelector("#login-password"),
  loginError: document.querySelector("#login-error"),
  loginSubmit: document.querySelector("#login-submit"),
  closeLogin: document.querySelector("#close-login"),
  volunteerZone: document.querySelector("#volunteer-zone-filter"),
  volunteerPool: document.querySelector("#volunteer-pool-filter"),
  volunteerStatus: document.querySelector("#volunteer-status-filter"),
  volunteerVisibleCount: document.querySelector("#volunteer-visible-count"),
  volunteerPendingCount: document.querySelector("#volunteer-pending-count"),
  volunteerAcceptedCount: document.querySelector("#volunteer-accepted-count"),
  volunteerDeployedCount: document.querySelector("#volunteer-deployed-count"),
  placementGapList: document.querySelector("#placement-gap-list"),
  volunteerCommandHeading: document.querySelector("#volunteer-command-heading"),
  volunteerCommandHelp: document.querySelector("#volunteer-command-help"),
  volunteerPlaceCommand: document.querySelector("#volunteer-place-command"),
  copyVolunteerCommand: document.querySelector("#copy-volunteer-command"),
  volunteerMap: document.querySelector("#volunteer-map"),
  volunteerMapHeading: document.querySelector("#coordinate-map-heading"),
  volunteerMapCaption: document.querySelector("#volunteer-map-caption"),
  volunteerPlacements: document.querySelector("#volunteer-placements"),
  volunteerEditDialog: document.querySelector("#volunteer-edit-dialog"),
  volunteerEditForm: document.querySelector("#volunteer-edit-form"),
  volunteerEditTitle: document.querySelector("#volunteer-edit-title"),
  volunteerEditPool: document.querySelector("#volunteer-edit-pool"),
  volunteerEditX: document.querySelector("#volunteer-edit-x"),
  volunteerEditY: document.querySelector("#volunteer-edit-y"),
  volunteerEditZ: document.querySelector("#volunteer-edit-z"),
  volunteerEditHeading: document.querySelector("#volunteer-edit-heading"),
  volunteerEditNote: document.querySelector("#volunteer-edit-note"),
  volunteerStatusField: document.querySelector("#volunteer-status-field"),
  volunteerEditStatus: document.querySelector("#volunteer-edit-status"),
  volunteerEditError: document.querySelector("#volunteer-edit-error"),
  volunteerEditClose: document.querySelector("#volunteer-edit-close"),
  volunteerDelete: document.querySelector("#volunteer-delete"),
  volunteerSave: document.querySelector("#volunteer-save"),
  toast: document.querySelector("#toast"),
};

function normalize(value) {
  return String(value ?? "").trim().toLowerCase();
}

function modelSearchText(model) {
  return normalize([
    model.id,
    model.assetFile,
    model.label,
    model.description,
    model.descriptor,
    ...(model.knownNames ?? []),
  ].join(" "));
}

function setView(name) {
  elements.tabs.forEach((tab) => {
    const active = tab.dataset.view === name;
    tab.classList.toggle("active", active);
    tab.setAttribute("aria-selected", String(active));
  });

  elements.views.forEach((view) => {
    const active = view.dataset.viewPanel === name;
    view.classList.toggle("active", active);
    view.hidden = !active;
  });
}

function applyFilters() {
  const query = normalize(elements.search.value);
  const category = elements.category.value;
  const fit = elements.fit.value;

  state.filtered = state.models.filter((model) => {
    if (category && model.category !== category) {
      return false;
    }
    if (fit && model.roleplayFit !== fit) {
      return false;
    }
    return !query || modelSearchText(model).includes(query);
  });

  state.visibleCount = PAGE_SIZE;
  renderModels();
}

function fitClass(value) {
  if (value === "Experimental") {
    return "experimental";
  }
  if (value === "Test scale/behavior") {
    return "test";
  }
  return "";
}

function modelContext(model) {
  if (model.knownNames?.length) {
    return model.knownNames.slice(0, 3).join(", ");
  }
  return model.description || model.descriptor || "No descriptive client metadata";
}

function renderModels() {
  const visible = state.filtered.slice(0, state.visibleCount);
  elements.results.replaceChildren();

  if (!visible.length) {
    const empty = document.createElement("p");
    empty.className = "empty-state";
    empty.textContent = "No models match those filters.";
    elements.results.append(empty);
  }

  visible.forEach((model) => {
    const row = document.createElement("button");
    row.type = "button";
    row.className = "model-row";
    row.classList.toggle("selected", state.selectedId === model.id);
    row.dataset.modelId = model.id;
    row.innerHTML = `
      <span class="model-id">${model.id}</span>
      <span>
        <span class="model-title">${escapeHtml(model.label)}</span>
        <span class="model-file">${escapeHtml(model.assetFile)}</span>
      </span>
      <span class="model-context">${escapeHtml(modelContext(model))}</span>
      <span class="fit-label ${fitClass(model.roleplayFit)}">${escapeHtml(model.roleplayFit)}</span>
    `;
    row.addEventListener("click", () => selectModel(model.id));
    elements.results.append(row);
  });

  elements.resultCount.textContent = `${state.filtered.length.toLocaleString()} model${state.filtered.length === 1 ? "" : "s"}`;
  elements.loadMore.hidden = state.visibleCount >= state.filtered.length;
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

function selectModel(id) {
  const model = state.models.find((candidate) => candidate.id === id);
  if (!model) {
    return;
  }

  state.selectedId = id;
  document.querySelectorAll(".model-row").forEach((row) => {
    row.classList.toggle("selected", Number(row.dataset.modelId) === id);
  });

  elements.detailCategory.textContent = model.category;
  elements.detailTitle.textContent = `${model.label} - ${model.id}`;
  elements.detailContext.textContent = modelContext(model);
  elements.detailMetadata.replaceChildren();

  const metadata = [
    ["Asset", model.assetFile],
    ["Known names", model.knownNames?.join(", ") || "None"],
    ["Roleplay fit", model.roleplayFit],
    ["Catalog scale", model.scale || "Client default"],
    ["NPC definitions", model.npcDefinitionIds?.join(", ") || "None"],
    ["Notes", model.notes || "No catalog warnings"],
  ];

  metadata.forEach(([term, value]) => {
    const dt = document.createElement("dt");
    const dd = document.createElement("dd");
    dt.textContent = term;
    dd.textContent = value;
    elements.detailMetadata.append(dt, dd);
  });

  const catalogImage = state.catalogImages.get(model.id);
  const imageUrl = catalogImage?.imageUrl || model.imageUrl;

  if (imageUrl) {
    elements.detailImage.src = imageUrl;
    elements.detailImage.alt = `${model.label} model preview`;
    elements.detailImage.hidden = false;
    elements.detailPlaceholder.hidden = true;
  } else {
    elements.detailImage.hidden = true;
    elements.detailPlaceholder.hidden = false;
  }

  if (catalogImage) {
    const date = new Date(catalogImage.updatedAt).toLocaleDateString(undefined, {
      year: "numeric",
      month: "short",
      day: "numeric",
    });
    elements.imageCredit.textContent = `Last updated by ${catalogImage.updatedBy} on ${date}`;
    elements.imageCredit.hidden = false;
    elements.chooseImage.textContent = "Replace model image";
  } else {
    elements.imageCredit.hidden = true;
    elements.chooseImage.textContent = "Add model image";
  }

  elements.commandBuilder.hidden = false;
  elements.spawnName.value = (model.knownNames?.[0] || model.label).slice(0, 40);
  updateSpawnCommand();
}

function renderAccount() {
  const signedIn = Boolean(state.user);
  elements.accountName.textContent = signedIn ? `Signed in as ${state.user.username}` : "";
  elements.accountName.hidden = !signedIn;
  elements.accountLogin.hidden = signedIn;
  elements.accountLogout.hidden = !signedIn;

  if (state.placements.length) {
    renderVolunteerPlacements();
  }
}

function populateVolunteerFilters() {
  const selectedZone = elements.volunteerZone.value;
  const selectedPool = elements.volunteerPool.value;
  const zones = [...new Set([
    ...collectionMaps.map((map) => map.zoneId),
    ...state.placements.map((placement) => placement.zoneId),
  ])]
    .sort((left, right) => left - right);
  const pools = [...new Set([
    ...collectionPoolKeys,
    ...state.placements.map((placement) => placement.pool),
  ])].sort();

  elements.volunteerZone.innerHTML = '<option value="">All zones</option>';
  zones.forEach((zoneId) => elements.volunteerZone.add(new Option(`Zone ${zoneId}`, zoneId)));
  elements.volunteerZone.value = zones.includes(Number(selectedZone)) ? selectedZone : "";

  elements.volunteerPool.innerHTML = '<option value="">All pools</option>';
  pools.forEach((pool) => elements.volunteerPool.add(new Option(pool, pool)));
  elements.volunteerPool.value = pools.includes(selectedPool) ? selectedPool : "";
}

function applyVolunteerFilters() {
  const zone = Number(elements.volunteerZone.value);
  const pool = elements.volunteerPool.value;
  const status = elements.volunteerStatus.value;

  state.filteredPlacements = state.placements.filter((placement) =>
    (!zone || placement.zoneId === zone) &&
    (!pool || placement.pool === pool) &&
    (!status || placement.status === status)
  );
  renderVolunteerPlacements();
}

function renderVolunteerPlacements() {
  const placements = state.filteredPlacements;
  elements.volunteerVisibleCount.textContent = placements.length.toLocaleString();
  elements.volunteerPendingCount.textContent = state.placements
    .filter((placement) => placement.status === "pending").length.toLocaleString();
  elements.volunteerAcceptedCount.textContent = state.placements
    .filter((placement) => placement.status === "accepted").length.toLocaleString();
  elements.volunteerDeployedCount.textContent = state.placements
    .filter((placement) => placement.status === "deployed").length.toLocaleString();
  renderPlacementGaps();
  renderVolunteerCommand();
  elements.volunteerPlacements.replaceChildren();

  if (!placements.length) {
    const row = document.createElement("tr");
    row.innerHTML = '<td colspan="6" class="empty-table">No submissions match these filters.</td>';
    elements.volunteerPlacements.append(row);
  }

  placements.forEach((placement) => {
    const row = document.createElement("tr");
    const note = placement.note
      ? `<span class="submission-note">${escapeHtml(placement.note)}</span>`
      : "";
    row.innerHTML = `
      <td><strong>#${placement.id}</strong></td>
      <td><strong>Zone ${placement.zoneId}</strong><span>${escapeHtml(placement.pool)}</span>${placement.hardPointId ? `<span>Live node #${placement.hardPointId}</span>` : ""}${note}</td>
      <td><code>${placement.x.toFixed(1)}, ${placement.y.toFixed(1)}, ${placement.z.toFixed(1)}</code></td>
      <td>${escapeHtml(placement.submittedBy)}</td>
      <td><span class="status-badge ${placement.status}">${escapeHtml(placement.status)}</span></td>
      <td></td>
    `;

    if (placement.canEdit) {
      const edit = document.createElement("button");
      edit.type = "button";
      edit.className = "table-action";
      edit.textContent = placement.canReview ? "Review" : "Edit";
      edit.addEventListener("click", () => openVolunteerEditor(placement.key));
      row.lastElementChild.append(edit);
    }

    elements.volunteerPlacements.append(row);
  });

  renderVolunteerMap();
}

function renderPlacementGaps() {
  if (!elements.placementGapList) {
    return;
  }

  const counts = new Map(collectionPoolKeys.map((pool) => [pool, 0]));
  state.placements.forEach((placement) => {
    if (counts.has(placement.pool)) {
      counts.set(placement.pool, counts.get(placement.pool) + 1);
    }
  });

  const gaps = [...counts.entries()]
    .filter(([, count]) => count === 0 || count < 10)
    .sort(([leftPool, leftCount], [rightPool, rightCount]) =>
      leftCount - rightCount || leftPool.localeCompare(rightPool))
    .slice(0, 14);

  elements.placementGapList.replaceChildren();
  if (!gaps.length) {
    const done = document.createElement("span");
    done.className = "placement-gap-chip healthy";
    done.textContent = "All live pools have 10+ placements";
    elements.placementGapList.append(done);
    return;
  }

  gaps.forEach(([pool, count]) => {
    const chip = document.createElement("button");
    chip.type = "button";
    chip.className = `placement-gap-chip ${count === 0 ? "empty" : "low"}`;
    chip.textContent = `${pool}: ${count}`;
    chip.addEventListener("click", () => {
      elements.volunteerPool.value = pool;
      elements.volunteerZone.value = "1";
      applyVolunteerFilters();
    });
    elements.placementGapList.append(chip);
  });
}

function renderVolunteerCommand() {
  const pool = elements.volunteerPool.value;
  if (!pool) {
    elements.volunteerCommandHeading.textContent = "Submit from your position";
    elements.volunteerCommandHelp.textContent = "Select a collection pool to get its exact placement command.";
    elements.volunteerPlaceCommand.textContent = "!volunteer place [pool] [optional note]";
    elements.copyVolunteerCommand.disabled = true;
    return;
  }

  const map = collectionMaps.find((candidate) => candidate.pools.includes(pool));
  elements.volunteerCommandHeading.textContent = map
    ? `Place a ${map.name} collection node`
    : "Place this collection node";
  elements.volunteerCommandHelp.textContent =
    "Stand where the node should appear and run this command. You can add a short landmark note after the pool key.";
  elements.volunteerPlaceCommand.textContent = `!volunteer place ${pool}`;
  elements.copyVolunteerCommand.disabled = false;
}

function renderVolunteerMap() {
  const svg = elements.volunteerMap;
  svg.replaceChildren();
  svg.setAttribute("viewBox", "0 0 800 480");
  svg.style.aspectRatio = "5 / 3";
  svg.classList.remove("regional-map");
  elements.volunteerMapHeading.textContent = "X/Z placement plot";
  const zones = [...new Set(state.filteredPlacements.map((placement) => placement.zoneId))];
  const selectedZone = Number(elements.volunteerZone.value);
  const selectedPool = elements.volunteerPool.value;
  const selectedMap = chooseCollectionMapForSelection(selectedZone, selectedPool);

  if (!state.filteredPlacements.length && selectedMap) {
    renderRegionalVolunteerMap(svg, [], selectedMap);
    return;
  }

  if (zones.length !== 1 || !state.filteredPlacements.length) {
    elements.volunteerMapCaption.textContent = zones.length > 1
      ? "Choose one zone to compare locations."
      : "No locations are available for this selection.";
    const message = document.createElementNS("http://www.w3.org/2000/svg", "text");
    message.setAttribute("x", "400");
    message.setAttribute("y", "240");
    message.setAttribute("text-anchor", "middle");
    message.setAttribute("class", "map-empty");
    message.textContent = zones.length > 1 ? "Select one zone" : "No placement data";
    svg.append(message);
    return;
  }

  const placements = state.filteredPlacements;
  const regionalMap = chooseCollectionMap(zones[0], placements, selectedPool);

  if (regionalMap) {
    renderRegionalVolunteerMap(svg, placements, regionalMap);
    return;
  }

  const xs = placements.map((placement) => placement.x);
  const zs = placements.map((placement) => placement.z);
  const minX = Math.min(...xs);
  const maxX = Math.max(...xs);
  const minZ = Math.min(...zs);
  const maxZ = Math.max(...zs);
  const rangeX = Math.max(maxX - minX, 10);
  const rangeZ = Math.max(maxZ - minZ, 10);
  const padding = 38;
  const plotWidth = 800 - padding * 2;
  const plotHeight = 480 - padding * 2;

  elements.volunteerMapCaption.textContent =
    `Zone ${zones[0]}: ${placements.length} location${placements.length === 1 ? "" : "s"}`;

  const background = document.createElementNS("http://www.w3.org/2000/svg", "rect");
  background.setAttribute("x", padding);
  background.setAttribute("y", padding);
  background.setAttribute("width", plotWidth);
  background.setAttribute("height", plotHeight);
  background.setAttribute("class", "map-plot");
  svg.append(background);

  placements.forEach((placement) => {
    const x = padding + ((placement.x - minX + (rangeX - (maxX - minX)) / 2) / rangeX) * plotWidth;
    const y = padding + plotHeight -
      ((placement.z - minZ + (rangeZ - (maxZ - minZ)) / 2) / rangeZ) * plotHeight;
    svg.append(createMapPoint(placement, x, y, "7"));
  });
}

function renderRegionalVolunteerMap(svg, placements, regionalMap) {
  const width = 800;
  const bounds = getProjectionBounds(regionalMap, placements);
  const height = width * (bounds.maxZ - bounds.minZ) / (bounds.maxX - bounds.minX);
  svg.setAttribute("viewBox", `0 0 ${width} ${height}`);
  svg.style.aspectRatio = `${width} / ${height}`;
  svg.classList.add("regional-map");

  elements.volunteerMapCaption.textContent =
    `${regionalMap.name}: ${placements.length} location${placements.length === 1 ? "" : "s"}`;
  elements.volunteerMapHeading.textContent = `${regionalMap.name} placement map`;

  const background = document.createElementNS("http://www.w3.org/2000/svg", "image");
  background.setAttribute("href", regionalMap.imageUrl);
  background.setAttribute("width", width);
  background.setAttribute("height", height);
  background.setAttribute("preserveAspectRatio", "none");
  svg.append(background);

  const inBounds = [];
  const outOfBounds = [];
  placements.forEach((placement) => {
    if (isPlacementInsideBounds(placement, bounds)) {
      inBounds.push(placement);
    } else {
      outOfBounds.push(placement);
    }
  });

  inBounds.forEach((placement) => {
    const { x, y } = projectToMap(placement, bounds, width, height);
    svg.append(createMapPoint(placement, x, y, "8"));
  });

  if (outOfBounds.length) {
    renderOutlierRibbon(svg, outOfBounds, width, height);
    elements.volunteerMapCaption.textContent +=
      `; ${outOfBounds.length} outside current map bounds`;
  }

  if (!placements.length) {
    renderMapEmptyState(svg, width, height, "No submissions yet");
  }

  if (regionalMap.autoFitPlacements && placements.length) {
    elements.volunteerMapCaption.textContent +=
      "; auto-fit view";
  }
}

function chooseCollectionMap(zoneId, placements, selectedPool = "") {
  if (selectedPool) {
    const selected = chooseCollectionMapForSelection(zoneId, selectedPool);
    if (selected) {
      return selected;
    }
  }

  const candidates = collectionMaps
    .filter((map) => map.zoneId === zoneId)
    .map((map) => {
      const poolMatches = placements.filter((placement) => map.pools.includes(placement.pool));
      const bounds = getProjectionBounds(map, poolMatches);
      const inBounds = poolMatches.filter((placement) => isPlacementInsideBounds(placement, bounds));
      return {
        map,
        poolMatchCount: poolMatches.length,
        inBoundsCount: inBounds.length,
      };
    })
    .filter((candidate) => candidate.poolMatchCount > 0)
    .sort((left, right) =>
      right.inBoundsCount - left.inBoundsCount ||
      right.poolMatchCount - left.poolMatchCount);

  return candidates[0]?.map ?? null;
}

function chooseCollectionMapForSelection(zoneId, pool) {
  if (!pool) {
    return null;
  }
  return collectionMaps.find((map) =>
    (!zoneId || map.zoneId === zoneId) &&
    map.pools.includes(pool)) ?? null;
}

function getProjectionBounds(map, placements) {
  if (!map.autoFitPlacements || !placements.length) {
    return {
      minX: map.minX,
      maxX: map.maxX,
      minZ: map.minZ,
      maxZ: map.maxZ,
    };
  }

  const xs = placements.map((placement) => placement.x);
  const zs = placements.map((placement) => placement.z);
  const minX = Math.min(...xs);
  const maxX = Math.max(...xs);
  const minZ = Math.min(...zs);
  const maxZ = Math.max(...zs);
  const xRange = Math.max(maxX - minX, 96);
  const zRange = Math.max(maxZ - minZ, 96);
  const paddingX = Math.max(xRange * 0.16, 48);
  const paddingZ = Math.max(zRange * 0.16, 48);

  return {
    minX: minX - paddingX,
    maxX: maxX + paddingX,
    minZ: minZ - paddingZ,
    maxZ: maxZ + paddingZ,
  };
}

function isPlacementInsideBounds(placement, bounds) {
  return (
    placement.x >= bounds.minX &&
    placement.x <= bounds.maxX &&
    placement.z >= bounds.minZ &&
    placement.z <= bounds.maxZ
  );
}

function projectToMap(placement, bounds, width, height) {
  return {
    x: (placement.x - bounds.minX) / (bounds.maxX - bounds.minX) * width,
    y: (placement.z - bounds.minZ) / (bounds.maxZ - bounds.minZ) * height,
  };
}

function renderMapEmptyState(svg, width, height, text) {
  const message = document.createElementNS("http://www.w3.org/2000/svg", "text");
  message.setAttribute("x", width / 2);
  message.setAttribute("y", height / 2);
  message.setAttribute("text-anchor", "middle");
  message.setAttribute("class", "map-empty");
  message.textContent = text;
  svg.append(message);
}

function createMapPoint(placement, x, y, radius) {
  const point = document.createElementNS("http://www.w3.org/2000/svg", "circle");
  point.setAttribute("cx", x);
  point.setAttribute("cy", y);
  point.setAttribute("r", radius);
  point.setAttribute("class", `map-point ${placement.status}`);
  point.setAttribute("tabindex", "0");
  const title = document.createElementNS("http://www.w3.org/2000/svg", "title");
  title.textContent =
    `#${placement.id} ${placement.pool} (${placement.x.toFixed(1)}, ${placement.y.toFixed(1)}, ${placement.z.toFixed(1)}) by ${placement.submittedBy}`;
  point.append(title);
  return point;
}

function renderOutlierRibbon(svg, placements, width, height) {
  const ribbon = document.createElementNS("http://www.w3.org/2000/svg", "g");
  ribbon.setAttribute("class", "map-outlier-ribbon");

  const background = document.createElementNS("http://www.w3.org/2000/svg", "rect");
  background.setAttribute("x", "12");
  background.setAttribute("y", height - 46);
  background.setAttribute("width", Math.min(width - 24, 560));
  background.setAttribute("height", "34");
  background.setAttribute("rx", "4");
  ribbon.append(background);

  const text = document.createElementNS("http://www.w3.org/2000/svg", "text");
  text.setAttribute("x", "28");
  text.setAttribute("y", height - 24);
  text.textContent = `${placements.length} point${placements.length === 1 ? "" : "s"} outside this map crop; check coordinates in the table.`;
  ribbon.append(text);

  svg.append(ribbon);
}

async function loadVolunteerPlacements() {
  try {
    const response = await fetch("/volunteers/placements/", {
      cache: "no-store",
      credentials: "same-origin",
    });
    if (!response.ok) {
      throw new Error(`Status ${response.status}`);
    }
    state.placements = await response.json();
    state.filteredPlacements = state.placements;
    populateVolunteerFilters();
    applyVolunteerFilters();
  } catch {
    state.placements = [];
    state.filteredPlacements = [];
    renderVolunteerPlacements();
    elements.volunteerMapCaption.textContent = "Placement service unavailable.";
  }
}

function openVolunteerEditor(key) {
  const placement = state.placements.find((candidate) => candidate.key === key);
  if (!placement?.canEdit) {
    return;
  }

  state.selectedPlacementId = placement.id;
  elements.volunteerEditTitle.textContent = `Edit submission #${placement.id}${placement.hardPointId ? ` / live node #${placement.hardPointId}` : ""}`;
  elements.volunteerEditPool.value = placement.pool;
  elements.volunteerEditX.value = placement.x;
  elements.volunteerEditY.value = placement.y;
  elements.volunteerEditZ.value = placement.z;
  elements.volunteerEditHeading.value = placement.heading;
  elements.volunteerEditNote.value = placement.note || "";
  elements.volunteerEditStatus.value = placement.status;
  elements.volunteerEditStatus.disabled = !placement.canReview;
  const materialized = Boolean(placement.hardPointId);
  elements.volunteerEditPool.disabled = materialized;
  elements.volunteerEditX.disabled = materialized;
  elements.volunteerEditY.disabled = materialized;
  elements.volunteerEditZ.disabled = materialized;
  elements.volunteerEditHeading.disabled = materialized;
  elements.volunteerDelete.hidden = materialized;
  elements.volunteerStatusField.hidden = !placement.canReview;
  elements.volunteerEditError.hidden = true;
  elements.volunteerEditDialog.showModal();
}

async function saveVolunteerPlacement(event) {
  event.preventDefault();
  const id = state.selectedPlacementId;
  const placement = state.placements.find((candidate) => candidate.id === id);
  if (!placement) {
    return;
  }

  elements.volunteerSave.disabled = true;
  elements.volunteerEditError.hidden = true;
  try {
    const response = await fetch(`/volunteers/placements/${id}`, {
      method: "PUT",
      credentials: "same-origin",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        pool: elements.volunteerEditPool.value,
        x: Number(elements.volunteerEditX.value),
        y: Number(elements.volunteerEditY.value),
        z: Number(elements.volunteerEditZ.value),
        heading: Number(elements.volunteerEditHeading.value),
        note: elements.volunteerEditNote.value || null,
        status: placement.canReview ? elements.volunteerEditStatus.value : placement.status,
      }),
    });
    if (!response.ok) {
      throw new Error(response.status === 403
        ? "This submission can no longer be edited."
        : "The submission could not be saved.");
    }
    elements.volunteerEditDialog.close();
    await loadVolunteerPlacements();
    showToast(`Submission #${id} saved.`);
  } catch (error) {
    elements.volunteerEditError.textContent = error.message;
    elements.volunteerEditError.hidden = false;
  } finally {
    elements.volunteerSave.disabled = false;
  }
}

async function deleteVolunteerPlacement() {
  const id = state.selectedPlacementId;
  if (!id || !confirm(`Delete collection submission #${id}?`)) {
    return;
  }

  try {
    const response = await fetch(`/volunteers/placements/${id}`, {
      method: "DELETE",
      credentials: "same-origin",
    });
    if (!response.ok) {
      throw new Error("This submission could not be deleted.");
    }

    elements.volunteerEditDialog.close();
    await loadVolunteerPlacements();
    showToast(`Submission #${id} deleted.`);
  } catch (error) {
    elements.volunteerEditError.textContent = error.message;
    elements.volunteerEditError.hidden = false;
  }
}

function openLogin() {
  elements.loginError.hidden = true;
  elements.loginError.textContent = "";
  elements.loginDialog.showModal();
  elements.loginUsername.focus();
}

async function loadSession() {
  try {
    const response = await fetch("catalog/session", {
      cache: "no-store",
      credentials: "same-origin",
    });
    state.user = response.ok ? await response.json() : null;
  } catch {
    state.user = null;
  }
  renderAccount();
}

async function login(event) {
  event.preventDefault();
  elements.loginError.hidden = true;
  elements.loginSubmit.disabled = true;
  elements.loginSubmit.textContent = "Signing in...";

  try {
    const response = await fetch("catalog/login", {
      method: "POST",
      credentials: "same-origin",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        username: elements.loginUsername.value,
        password: elements.loginPassword.value,
      }),
    });

    if (!response.ok) {
      throw new Error(response.status === 429
        ? "Too many sign-in attempts. Wait a minute and try again."
        : "That username or password was not accepted.");
    }

    state.user = await response.json();
    elements.loginPassword.value = "";
    elements.loginDialog.close();
    renderAccount();
    await loadVolunteerPlacements();
    showToast(`Signed in as ${state.user.username}.`);
  } catch (error) {
    elements.loginError.textContent = error.message;
    elements.loginError.hidden = false;
  } finally {
    elements.loginSubmit.disabled = false;
    elements.loginSubmit.textContent = "Sign in";
  }
}

async function logout() {
  try {
    await fetch("catalog/logout", {
      method: "POST",
      credentials: "same-origin",
    });
  } finally {
    state.user = null;
    renderAccount();
    await loadVolunteerPlacements();
    showToast("Signed out.");
  }
}

async function uploadSelectedImage() {
  const file = elements.imageFile.files?.[0];
  elements.imageFile.value = "";

  if (!file || !state.selectedId) {
    return;
  }

  if (file.size > 4 * 1024 * 1024) {
    showToast("Choose an image no larger than 4 MB.");
    return;
  }

  if (!["image/jpeg", "image/png", "image/webp"].includes(file.type)) {
    showToast("Use a JPEG, PNG, or WebP image.");
    return;
  }

  const modelId = state.selectedId;
  const form = new FormData();
  form.append("image", file);
  elements.chooseImage.disabled = true;
  elements.chooseImage.textContent = "Optimizing image...";

  try {
    const response = await fetch(`catalog/models/${modelId}/image`, {
      method: "POST",
      credentials: "same-origin",
      body: form,
    });

    if (response.status === 401) {
      state.user = null;
      renderAccount();
      openLogin();
      return;
    }

    const payload = await response.json().catch(() => ({}));
    if (!response.ok) {
      throw new Error(payload.error || (response.status === 429
        ? "Upload limit reached. Try again later."
        : "The image could not be saved."));
    }

    state.catalogImages.set(modelId, payload);
    selectModel(modelId);
    showToast("Model image saved.");
  } catch (error) {
    showToast(error.message);
  } finally {
    if (state.selectedId === modelId) {
      elements.chooseImage.disabled = false;
      elements.chooseImage.textContent = state.catalogImages.has(modelId)
        ? "Replace model image"
        : "Add model image";
    }
  }
}

function safeGroup(value) {
  return normalize(value).replace(/^@/, "").replace(/[^a-z0-9_-]/g, "").slice(0, 24) || "scene";
}

function safeName(value) {
  return String(value ?? "").replace(/[\r\n]/g, " ").trim().slice(0, 40) || "Miniature";
}

function updateSpawnCommand() {
  if (!state.selectedId) {
    return;
  }
  elements.spawnCommand.textContent =
    `!mini spawn ${state.selectedId} @${safeGroup(elements.spawnGroup.value)} ${safeName(elements.spawnName.value)}`;
}

async function copyText(value, successMessage) {
  try {
    await navigator.clipboard.writeText(value);
    showToast(successMessage);
  } catch {
    showToast("Clipboard access was blocked. Select the command and copy it manually.");
  }
}

let toastTimer;
function showToast(message) {
  clearTimeout(toastTimer);
  elements.toast.textContent = message;
  elements.toast.hidden = false;
  toastTimer = setTimeout(() => {
    elements.toast.hidden = true;
  }, 2600);
}

function renderCommands() {
  elements.commandList.replaceChildren();
  state.commands.forEach((command) => {
    const entry = document.createElement("article");
    entry.className = "command-entry";
    entry.innerHTML = `
      <h3>${escapeHtml(command.task)}</h3>
      <code>${escapeHtml(command.syntax)}</code>
      <p>${escapeHtml(command.description)}</p>
      <button class="secondary-button" type="button">Copy example</button>
    `;
    entry.querySelector("button").addEventListener("click", () =>
      copyText(command.example, `Copied: ${command.example}`)
    );
    elements.commandList.append(entry);
  });
}

function populateCategoryFilter(categories) {
  categories.forEach((category) => {
    const option = document.createElement("option");
    option.value = category;
    option.textContent = category;
    elements.category.append(option);
  });
}

async function loadServerStats(catalog) {
  document.querySelector("#stat-models").textContent = catalog.totalModels.toLocaleString();
  document.querySelector("#stat-aliases").textContent = catalog.modelsWithKnownNames.toLocaleString();

  const status = document.querySelector("#service-status");
  try {
    const response = await fetch("stats", { cache: "no-store" });
    if (!response.ok) {
      throw new Error(`Status ${response.status}`);
    }
    const stats = await response.json();
    document.querySelector("#stat-accounts").textContent = stats.registeredAccounts.toLocaleString();
    document.querySelector("#stat-characters").textContent = stats.createdCharacters.toLocaleString();
    status.classList.add("online");
    status.textContent = "Web API online";
    status.prepend(Object.assign(document.createElement("span"), { className: "status-dot" }));
  } catch {
    status.classList.add("offline");
    status.textContent = "Live statistics unavailable";
    status.prepend(Object.assign(document.createElement("span"), { className: "status-dot" }));
  }
}

async function initialize() {
  try {
    const [catalogResponse, commandResponse, imageResponse] = await Promise.all([
      fetch("data/models.json"),
      fetch("data/commands.json"),
      fetch("catalog/models", { cache: "no-store" }).catch(() => null),
    ]);

    if (!catalogResponse.ok || !commandResponse.ok) {
      throw new Error("Catalog files could not be loaded.");
    }

    const catalog = await catalogResponse.json();
    state.models = catalog.models;
    state.commands = await commandResponse.json();
    if (imageResponse?.ok) {
      const images = await imageResponse.json();
      state.catalogImages = new Map(images.map((image) => [image.modelId, image]));
    }
    state.filtered = state.models;
    populateCategoryFilter(catalog.categories);
    renderModels();
    renderCommands();
    loadServerStats(catalog);

    const requestedId = Number(new URLSearchParams(location.search).get("model"));
    if (requestedId && state.models.some((model) => model.id === requestedId)) {
      selectModel(requestedId);
    } else if (state.models.length) {
      selectModel(state.models[0].id);
    }

    await loadSession();
    await loadVolunteerPlacements();
  } catch (error) {
    elements.resultCount.textContent = "Catalog unavailable";
    elements.results.innerHTML = `<p class="empty-state">${escapeHtml(error.message)}</p>`;
  }
}

elements.tabs.forEach((tab) => tab.addEventListener("click", () => setView(tab.dataset.view)));
[elements.search, elements.category, elements.fit].forEach((control) =>
  control.addEventListener("input", applyFilters)
);
[elements.volunteerZone, elements.volunteerPool, elements.volunteerStatus].forEach((control) =>
  control.addEventListener("input", applyVolunteerFilters)
);
elements.loadMore.addEventListener("click", () => {
  state.visibleCount += PAGE_SIZE;
  renderModels();
});
elements.spawnName.addEventListener("input", updateSpawnCommand);
elements.spawnGroup.addEventListener("input", updateSpawnCommand);
elements.copySpawn.addEventListener("click", () =>
  copyText(elements.spawnCommand.textContent, "Spawn command copied.")
);
elements.copyVolunteerCommand.addEventListener("click", () =>
  copyText(elements.volunteerPlaceCommand.textContent, "Placement command copied.")
);
elements.accountLogin.addEventListener("click", openLogin);
elements.accountLogout.addEventListener("click", logout);
elements.closeLogin.addEventListener("click", () => elements.loginDialog.close());
elements.loginForm.addEventListener("submit", login);
elements.volunteerEditClose.addEventListener("click", () => elements.volunteerEditDialog.close());
elements.volunteerEditForm.addEventListener("submit", saveVolunteerPlacement);
elements.volunteerDelete.addEventListener("click", deleteVolunteerPlacement);
elements.chooseImage.addEventListener("click", () => {
  if (!state.user) {
    openLogin();
    return;
  }
  elements.imageFile.click();
});
elements.imageFile.addEventListener("change", uploadSelectedImage);
elements.detailImage.addEventListener("error", () => {
  elements.detailImage.hidden = true;
  elements.detailPlaceholder.hidden = false;
});

initialize();
