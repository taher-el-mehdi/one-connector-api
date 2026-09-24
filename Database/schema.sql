
--
-- Structure de la table `logs`
--

CREATE TABLE `logs` (
  `id` char(36) NOT NULL,
  `id_synchronization` int(11) DEFAULT NULL,
  `status` tinyint(4) NOT NULL,
  `created_at` datetime(6) NOT NULL,
  `updated_at` datetime(6) NOT NULL,
  `last_attempt_at` datetime(6) DEFAULT NULL,
  `last_success_at` datetime(6) DEFAULT NULL,
  `retry_count` int(11) NOT NULL DEFAULT 0,
  `error_message` text DEFAULT NULL,
  `file` json DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------

--
-- Structure de la table `mapping_field`
--

CREATE TABLE `mapping_field` (
  `id` int(11) NOT NULL,
  `id_mapping_table` int(11) NOT NULL,
  `entity_field_name` varchar(256) NOT NULL,
  `cabinet_field_name` varchar(256) NOT NULL,
  `entity_type_name` varchar(128) DEFAULT NULL,
  `cabinet_type_name` varchar(128) DEFAULT NULL,
  `entity_type_long` int(11) DEFAULT NULL,
  `cabinet_type_long` int(11) DEFAULT NULL,
  `created_by` char(36) NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_by` char(36) NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000'
) ;

-- --------------------------------------------------------

--
-- Structure de la table `mapping_table`
--

CREATE TABLE `mapping_table` (
  `id` int(11) NOT NULL,
  `entity_name` varchar(256) NOT NULL,
  `cabinet_name` varchar(256) NOT NULL,
  `created_by` char(36) NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_by` char(36) NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------

--
-- Structure de la table `setting`
--

CREATE TABLE `setting` (
  `type` varchar(32) NOT NULL,
  `code` varchar(64) NOT NULL,
  `description` varchar(512) DEFAULT NULL,
  `key` varchar(64) NOT NULL,
  `value` varchar(2048) DEFAULT NULL,
  `created_by` char(36) NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_by` char(36) NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
  `configured` tinyint(1) NOT NULL DEFAULT 0,
  `status` tinyint(1) NOT NULL DEFAULT 0,
  `required` tinyint(1) NOT NULL DEFAULT 1
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------

--
-- Structure de la table `synchronization`
--

CREATE TABLE `synchronization` (
  `id` int(11) NOT NULL,
  `direction` varchar(16) NOT NULL,
  `source` varchar(256) NOT NULL,
  `destination` varchar(256) NOT NULL,
  `created_by` char(36) NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_by` char(36) NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
  `id_mapping_table` int(11) NOT NULL,
  `code` varchar(64) NOT NULL,
  `description` varchar(512) DEFAULT NULL,
  `status` varchar(16) DEFAULT NULL,
  `max_retries` int(11) NOT NULL DEFAULT 3,
  `timeout_seconds` int(11) NOT NULL DEFAULT 300,
  `next_run_at` datetime(6) DEFAULT NULL,
  `retry_count` int(11) NOT NULL DEFAULT 0
) ;

-- --------------------------------------------------------

--
-- Structure de la table `synchronization_filter`
--

CREATE TABLE `synchronization_filter` (
  `id` int(11) NOT NULL,
  `synchronization_id` int(11) NOT NULL,
  `field_name` varchar(128) NOT NULL,
  `operator` varchar(16) NOT NULL,
  `value` varchar(512) DEFAULT NULL,
  `logical_operator` varchar(8) NOT NULL DEFAULT 'AND',
  `sort_order` int(11) NOT NULL DEFAULT 0,
  `created_by` char(36) NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_by` char(36) NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------

--
-- Structure de la table `user`
--

CREATE TABLE `user` (
  `id` char(36) NOT NULL,
  `username` varchar(128) NOT NULL,
  `password_hash` varchar(512) NOT NULL,
  `display_name` varchar(256) DEFAULT NULL,
  `email` varchar(256) DEFAULT NULL,
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `created_at` datetime(6) NOT NULL,
  `updated_at` datetime(6) NOT NULL,
  `last_login_at` datetime(6) DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

--
-- Index pour les tables déchargées
--

--
-- Index pour la table `logs`
--
ALTER TABLE `logs`
  ADD PRIMARY KEY (`id`),
  ADD KEY `ix_logs_updated` (`updated_at`),
  ADD KEY `ix_logs_status` (`status`,`updated_at`),
  ADD KEY `ix_logs_synchronization` (`id_synchronization`);

--
-- Index pour la table `mapping_field`
--
ALTER TABLE `mapping_field`
  ADD PRIMARY KEY (`id`),
  ADD UNIQUE KEY `uq_oc_mapping_field_entity_field` (`id_mapping_table`,`entity_field_name`);

--
-- Index pour la table `mapping_table`
--
ALTER TABLE `mapping_table`
  ADD PRIMARY KEY (`id`),
  ADD UNIQUE KEY `uq_oc_mapping_table_entity_cabinet` (`entity_name`,`cabinet_name`);

--
-- Index pour la table `setting`
--
ALTER TABLE `setting`
  ADD PRIMARY KEY (`type`,`code`,`key`);

--
-- Index pour la table `synchronization`
--
ALTER TABLE `synchronization`
  ADD PRIMARY KEY (`id`),
  ADD UNIQUE KEY `uq_synchronization_code` (`code`),
  ADD KEY `fk_synchronization_mapping` (`id_mapping_table`),
  ADD KEY `ix_synchronization_due` (`next_run_at`);

--
-- Index pour la table `synchronization_filter`
--
ALTER TABLE `synchronization_filter`
  ADD PRIMARY KEY (`id`),
  ADD KEY `ix_sync_filter_synchronization` (`synchronization_id`);

--
-- Index pour la table `user`
--
ALTER TABLE `user`
  ADD PRIMARY KEY (`id`),
  ADD UNIQUE KEY `uq_user_username` (`username`);

--
-- AUTO_INCREMENT pour les tables déchargées
--

--
-- AUTO_INCREMENT pour la table `mapping_field`
--
ALTER TABLE `mapping_field`
  MODIFY `id` int(11) NOT NULL AUTO_INCREMENT;

--
-- AUTO_INCREMENT pour la table `mapping_table`
--
ALTER TABLE `mapping_table`
  MODIFY `id` int(11) NOT NULL AUTO_INCREMENT;

--
-- AUTO_INCREMENT pour la table `synchronization`
--
ALTER TABLE `synchronization`
  MODIFY `id` int(11) NOT NULL AUTO_INCREMENT;

--
-- AUTO_INCREMENT pour la table `synchronization_filter`
--
ALTER TABLE `synchronization_filter`
  MODIFY `id` int(11) NOT NULL AUTO_INCREMENT;

--
-- Contraintes pour les tables déchargées
--

--
-- Contraintes pour la table `logs`
--
ALTER TABLE `logs`
  ADD CONSTRAINT `fk_logs_synchronization` FOREIGN KEY (`id_synchronization`) REFERENCES `synchronization` (`id`) ON DELETE SET NULL ON UPDATE CASCADE;

--
-- Contraintes pour la table `mapping_field`
--
ALTER TABLE `mapping_field`
  ADD CONSTRAINT `fk_mapping_field_table` FOREIGN KEY (`id_mapping_table`) REFERENCES `mapping_table` (`id`) ON DELETE CASCADE;

--
-- Contraintes pour la table `synchronization`
--
ALTER TABLE `synchronization`
  ADD CONSTRAINT `fk_synchronization_mapping` FOREIGN KEY (`id_mapping_table`) REFERENCES `mapping_table` (`id`);

--
-- Contraintes pour la table `synchronization_filter`
--
ALTER TABLE `synchronization_filter`
  ADD CONSTRAINT `fk_sync_filter_synchronization` FOREIGN KEY (`synchronization_id`) REFERENCES `synchronization` (`id`) ON DELETE CASCADE;
COMMIT;

/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
